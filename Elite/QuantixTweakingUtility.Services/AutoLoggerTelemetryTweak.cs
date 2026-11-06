using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using QuantixTweakingUtility.Models;

namespace QuantixTweakingUtility.Services;

public record TelemetryLogger(string Name, Guid Id, int? Start);
public interface ITelemetryLoggerStore
{
    IReadOnlyList<TelemetryLogger> Read();
    bool Running(TelemetryLogger logger);
    void SetBoot(TelemetryLogger logger, int? start);
    void Stop(TelemetryLogger logger);
}

public sealed class WindowsTelemetryLoggerStore : ITelemetryLoggerStore
{
    private static readonly string[] Names = { "SQMLogger", "AutoLogger-Diagtrack-Listener" };
    private const string Root = @"SYSTEM\CurrentControlSet\Control\WMI\Autologger\";
    public IReadOnlyList<TelemetryLogger> Read()
    {
        var result = new List<TelemetryLogger>();
        foreach (string name in Names)
        {
            using var key = Registry.LocalMachine.OpenSubKey(Root + name);
            if (key == null) continue;
            if (!Guid.TryParse(key.GetValue("Guid") as string, out var id)) throw new IOException("The telemetry logger has no valid session identity: " + name);
            var value = key.GetValue("Start");
            if (value != null && (key.GetValueKind("Start") != RegistryValueKind.DWord || value is not int start || start is not (0 or 1)))
                throw new IOException("The telemetry logger has an unsupported boot configuration: " + name);
            result.Add(new(name, id, value as int?));
        }
        return result;
    }
    public void SetBoot(TelemetryLogger logger, int? start)
    {
        Validate(logger);
        if (start is not null and not 0 and not 1) throw new IOException("Invalid telemetry logger boot value.");
        using var key = Registry.LocalMachine.OpenSubKey(Root + logger.Name, true) ?? throw new IOException("The original telemetry logger is missing.");
        if (!Guid.TryParse(key.GetValue("Guid") as string, out var id) || id != logger.Id) throw new IOException("The telemetry logger identity changed. Backup retained.");
        if (start.HasValue) key.SetValue("Start", start.Value, RegistryValueKind.DWord); else key.DeleteValue("Start", false);
        key.Flush();
        var after = Read().SingleOrDefault(item => item.Name == logger.Name);
        if (after == null || after.Id != logger.Id || after.Start != start) throw new IOException("Telemetry logger boot readback failed. Backup retained.");
    }
    public bool Running(TelemetryLogger logger) { Validate(logger); return Control(logger, false); }
    public void Stop(TelemetryLogger logger) { Validate(logger); if (Control(logger, false)) Control(logger, true); }
    private static bool Control(TelemetryLogger logger, bool stop)
    {
        int size = Marshal.SizeOf<TraceProperties>();
        var buffer = Marshal.AllocHGlobal(65536);
        try
        {
            Marshal.Copy(new byte[65536], 0, buffer, 65536);
            var properties = new TraceProperties { Wnode = new WnodeHeader { BufferSize = 65536, Flags = 0x20000 }, LoggerNameOffset = (uint)size, LogFileNameOffset = (uint)(size + 2048) };
            Marshal.StructureToPtr(properties, buffer, false);
            uint result = ControlTrace(0, logger.Name, buffer, 0); // QUERY
            if (result == 4201) return false; // ERROR_WMI_INSTANCE_NOT_FOUND
            if (result != 0) throw new Win32Exception((int)result, "Cannot inspect telemetry trace session " + logger.Name + ".");
            properties = Marshal.PtrToStructure<TraceProperties>(buffer);
            if (properties.Wnode.Guid != logger.Id) throw new IOException("A different trace session uses this logger name. No session stopped.");
            if (!stop) return true;
            result = ControlTrace(0, logger.Name, buffer, 1); // STOP
            if (result != 0 && result != 4201) throw new Win32Exception((int)result, "Cannot stop telemetry trace session " + logger.Name + ".");
            return false;
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }
    private static void Validate(TelemetryLogger logger)
    {
        if (!Names.Contains(logger.Name, StringComparer.Ordinal) || logger.Id == Guid.Empty) throw new IOException("Invalid telemetry logger target.");
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct WnodeHeader { public uint BufferSize, ProviderId; public ulong HistoricalContext; public long TimeStamp; public Guid Guid; public uint ClientContext, Flags; }
    [StructLayout(LayoutKind.Sequential)]
    private struct TraceProperties
    {
        public WnodeHeader Wnode;
        public uint BufferSize, MinimumBuffers, MaximumBuffers, MaximumFileSize, LogFileMode, FlushTimer, EnableFlags;
        public int AgeLimit;
        public uint NumberOfBuffers, FreeBuffers, EventsLost, BuffersWritten, LogBuffersLost, RealTimeBuffersLost;
        public IntPtr LoggerThreadId;
        public uint LogFileNameOffset, LoggerNameOffset;
    }
    [DllImport("advapi32.dll", EntryPoint = "ControlTraceW", CharSet = CharSet.Unicode)]
    private static extern uint ControlTrace(ulong handle, string name, IntPtr properties, uint controlCode);
}

public static class AutoLoggerTelemetryTweak
{
    private const string Id = "processes.cleanup.autologger";
    public static TweakDefinition Build(IRestoreService restore, ITelemetryLoggerStore? store = null)
    {
        store ??= new WindowsTelemetryLoggerStore();
        var gate = new SemaphoreSlim(1, 1);
        return new TweakDefinition
        {
            Id = Id, Name = "AutoLogger Telemetry Off", Category = TweakCategory.Windows,
            Description = "Disables boot startup for the SQMLogger and AutoLogger-Diagtrack-Listener telemetry sessions when present, and stops their currently running ETW sessions after checking the session GUID. Other ETW logs stay unchanged. Diagnostic telemetry collection is reduced. Restore returns the saved boot settings; restart Windows to resume boot-time logging.",
            RiskLevel = TweakRiskLevel.Medium, RequiresAdministrator = true,
            AffectedSettings = new[] { "ETW:SQMLogger:Start", "ETW:AutoLogger-Diagtrack-Listener:Start" },
            CurrentStateReader = () => Task.Run(() =>
            {
                var loggers = store.Read();
                return loggers.Count == 0 ? TweakState.Unsupported : loggers.All(item => item.Start == 0 && !store.Running(item)) ? TweakState.Applied : TweakState.NotApplied;
            }),
            ApplyAction = () => Change(true), RestoreAction = () => Change(false)
        };
        async Task<TweakActionResult> Change(bool apply)
        {
            await gate.WaitAsync();
            try
            {
                return await Task.Run(() =>
                {
                    string? backup = restore.GetBackup(Id);
                    var current = store.Read();
                    TelemetryLogger[]? original = backup == null ? null : JsonSerializer.Deserialize<TelemetryLogger[]>(backup);
                    if (backup != null && (original == null || original.Length == 0 || original.Any(item => item == null) || original.Select(item => item.Name).Distinct().Count() != original.Length
                        || original.Any(item => item.Name is not ("SQMLogger" or "AutoLogger-Diagtrack-Listener") || item.Id == Guid.Empty || item.Start is not null and not 0 and not 1)))
                        throw new IOException("Invalid telemetry logger backup. No changes made.");
                    if (!apply && original == null) return TweakActionResult.Fail("No telemetry logger originals were saved by Elite.");
                    if (apply && current.Count == 0) return TweakActionResult.Fail("Neither supported telemetry logger exists on this PC.");
                    if (apply && original != null && (current.Any(item => !original.Any(saved => saved.Name == item.Name && saved.Id == item.Id))
                        || original.Any(saved => !current.Any(item => item.Name == saved.Name && item.Id == saved.Id))))
                        throw new IOException("The telemetry logger set changed. Original backup retained.");
                    if (apply && original != null && current.Any(item => item.Start != 0 && item.Start != original.Single(saved => saved.Name == item.Name).Start))
                        throw new IOException("A telemetry logger boot setting changed outside Elite. Original backup retained.");
                    if (apply && original == null)
                    {
                        // Check permissions/session identity before changing boot configuration.
                        foreach (var item in current) _ = store.Running(item);
                        restore.SaveBackup(Id, JsonSerializer.Serialize(current));
                        original = JsonSerializer.Deserialize<TelemetryLogger[]>(restore.GetBackup(Id) ?? "null");
                        if (original == null || !original.SequenceEqual(current)) throw new IOException("Telemetry backup verification failed. No changes made.");
                    }
                    int done = 0; var errors = new List<string>();
                    foreach (var saved in original!)
                    {
                        try
                        {
                            var now = store.Read().SingleOrDefault(item => item.Name == saved.Name);
                            if (now == null || now.Id != saved.Id || (now.Start != 0 && now.Start != saved.Start))
                                throw new IOException("Telemetry logger changed since its original was saved. Original backup retained.");
                            store.SetBoot(saved, apply ? 0 : saved.Start);
                            if (apply) { store.Stop(saved); if (store.Running(saved)) throw new IOException("Trace session remains active. Original backup retained."); }
                            var after = store.Read().SingleOrDefault(item => item.Name == saved.Name);
                            if (after == null || after.Id != saved.Id || after.Start != (apply ? 0 : saved.Start)) throw new IOException("Telemetry logger readback failed. Original backup retained.");
                            done++;
                        }
                        catch (Exception ex) when (ex is IOException or Win32Exception or UnauthorizedAccessException or System.Security.SecurityException)
                        { errors.Add(saved.Name + ": " + ex.Message); }
                    }
                    if (!apply && errors.Count == 0) restore.ClearBackup(Id);
                    string message = done + " telemetry logger configurations " + (apply ? "disabled; matching active sessions stopped." : "restored. Restart Windows to resume boot-time logging.");
                    return errors.Count == 0 ? TweakActionResult.Ok(message) : TweakActionResult.Fail(message + " Remaining originals retained. " + string.Join(" ", errors));
                });
            }
            finally { gate.Release(); }
        }
    }
}
