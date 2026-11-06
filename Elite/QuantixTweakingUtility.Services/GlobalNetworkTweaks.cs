using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using QuantixTweakingUtility.Models;

namespace QuantixTweakingUtility.Services;

public enum GlobalNetworkSetting { TaskOffload, Teredo, Isatap, Timestamps, Ecn, Rss }
public interface IGlobalNetworkStore
{
    Task<string> Read(GlobalNetworkSetting setting);
    Task Set(GlobalNetworkSetting setting, string value);
}

public sealed class WindowsGlobalNetworkStore : IGlobalNetworkStore
{
    private const string TcpPath = @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters";
    public async Task<string> Read(GlobalNetworkSetting setting)
    {
        string value;
        switch (setting)
        {
            case GlobalNetworkSetting.TaskOffload:
                using (var key = Registry.LocalMachine.OpenSubKey(TcpPath))
                {
                    object? raw = key?.GetValue("DisableTaskOffload");
                    if (raw == null) return "Unset";
                    if (key!.GetValueKind("DisableTaskOffload") != RegistryValueKind.DWord || raw is not int number || number is not (0 or 1))
                        throw new IOException("Task offload has an unsupported registry configuration.");
                    value = number == 1 ? "Disabled" : "Enabled";
                }
                break;
            case GlobalNetworkSetting.Teredo:
                value = (await WindowsAdapterRssStore.Run("[string](Get-NetTeredoConfiguration -PolicyStore PersistentStore -ErrorAction Stop).Type")).Trim();
                break;
            case GlobalNetworkSetting.Isatap:
                value = (await WindowsAdapterRssStore.Run("[string](Get-NetIsatapConfiguration -PolicyStore PersistentStore -ErrorAction Stop).State")).Trim();
                break;
            case GlobalNetworkSetting.Timestamps:
                value = ParseTimestamps(await Netsh("interface", "tcp", "dump"));
                break;
            case GlobalNetworkSetting.Ecn:
            case GlobalNetworkSetting.Rss:
                value = ParseTcpBoolean(await Netsh("interface", "tcp", "dump"), setting);
                break;
            default: throw new ArgumentOutOfRangeException(nameof(setting));
        }
        Validate(setting, value); return value;
    }
    public async Task Set(GlobalNetworkSetting setting, string value)
    {
        Validate(setting, value);
        switch (setting)
        {
            case GlobalNetworkSetting.TaskOffload:
                if (value == "Unset")
                {
                    using var existing = Registry.LocalMachine.OpenSubKey(TcpPath, true);
                    existing?.DeleteValue("DisableTaskOffload", false);
                }
                else
                {
                    using var key = Registry.LocalMachine.CreateSubKey(TcpPath, true);
                    key.SetValue("DisableTaskOffload", value == "Disabled" ? 1 : 0, RegistryValueKind.DWord);
                    key.Flush();
                }
                break;
            case GlobalNetworkSetting.Teredo:
                await WindowsAdapterRssStore.Run("Set-NetTeredoConfiguration -PolicyStore PersistentStore -Type " + value + " -ErrorAction Stop");
                break;
            case GlobalNetworkSetting.Isatap:
                await WindowsAdapterRssStore.Run("Set-NetIsatapConfiguration -PolicyStore PersistentStore -State " + value + " -ErrorAction Stop");
                break;
            case GlobalNetworkSetting.Timestamps:
                await Netsh("interface", "tcp", "set", "global", "timestamps=" + value.ToLowerInvariant());
                break;
            case GlobalNetworkSetting.Ecn:
            case GlobalNetworkSetting.Rss:
                await Netsh("interface", "tcp", "set", "global", (setting == GlobalNetworkSetting.Ecn ? "ecncapability=" : "rss=") + value.ToLowerInvariant());
                break;
        }
        if (await Read(setting) != value) throw new IOException("Global network setting readback failed. Original backup retained.");
    }
    public static string ParseTimestamps(string dump)
    {
        // Read the invariant command syntax, not localized status labels. Never execute a dump, which contains reset commands.
        var matches = Regex.Matches(dump, @"(?im)^\s*set\s+global\b[^\r\n]*\btimestamps=(disabled|enabled|allowed)\b");
        if (matches.Count != 1) throw new IOException("A single TCP timestamps configuration was not available.");
        return matches[0].Groups[1].Value.ToLowerInvariant() switch { "disabled" => "Disabled", "enabled" => "Enabled", _ => "Allowed" };
    }
    public static void Validate(GlobalNetworkSetting setting, string value)
    {
        string[] allowed = setting switch
        {
            GlobalNetworkSetting.TaskOffload => new[] { "Unset", "Enabled", "Disabled" },
            GlobalNetworkSetting.Teredo => new[] { "Default", "Relay", "Client", "Server", "Disabled", "Automatic", "Enterpriseclient", "Natawareclient" },
            GlobalNetworkSetting.Isatap => new[] { "Default", "Enabled", "Disabled" },
            GlobalNetworkSetting.Timestamps => new[] { "Allowed", "Enabled", "Disabled" },
            GlobalNetworkSetting.Ecn or GlobalNetworkSetting.Rss => new[] { "Enabled", "Disabled" },
            _ => throw new IOException("Unknown global network setting.")
        };
        if (!allowed.Contains(value, StringComparer.Ordinal)) throw new IOException("Unsupported global network value. No changes made.");
    }
    public static string ParseTcpBoolean(string dump, GlobalNetworkSetting setting)
    {
        string token = setting switch { GlobalNetworkSetting.Ecn => "ecncapability", GlobalNetworkSetting.Rss => "rss", _ => throw new IOException("Unknown TCP Boolean setting.") };
        var lines = Regex.Matches(dump, @"(?im)^\s*set\s+global\b[^\r\n]*\r?$");
        var values = lines.Cast<Match>().SelectMany(line => Regex.Matches(line.Value, @"(?i)(?<!\S)" + token + @"=([^\s]+)").Cast<Match>()).ToArray();
        if (values.Length != 1) throw new IOException("A single TCP " + token + " configuration was not available.");
        return values[0].Groups[1].Value.ToLowerInvariant() switch { "enabled" => "Enabled", "disabled" => "Disabled", _ => throw new IOException("Unsupported TCP " + token + " configuration.") };
    }
    private static async Task<string> Netsh(params string[] arguments)
    {
        var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "netsh.exe"))
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new IOException("Cannot start the Windows network configuration tool.");
        var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) { process.Kill(); await process.WaitForExitAsync(); await Task.WhenAll(output, error); throw new IOException("Network configuration timed out. Original backup retained."); }
        if (process.ExitCode != 0) throw new IOException("Windows rejected the network command. " + (await error).Trim() + " " + (await output).Trim());
        return await output;
    }
}

public static class GlobalNetworkTweaks
{
    public static TweakDefinition Build(GlobalNetworkSetting setting, IRestoreService restore, IGlobalNetworkStore? store = null)
    {
        store ??= new WindowsGlobalNetworkStore();
        if (setting is GlobalNetworkSetting.Isatap or GlobalNetworkSetting.Rss) throw new ArgumentException("This setting is part of a combined card.");
        var keys = setting == GlobalNetworkSetting.Teredo ? new[] { GlobalNetworkSetting.Teredo, GlobalNetworkSetting.Isatap } : setting == GlobalNetworkSetting.Ecn ? new[] { GlobalNetworkSetting.Ecn, GlobalNetworkSetting.Rss } : new[] { setting };
        string appliedValue = setting == GlobalNetworkSetting.Ecn ? "Enabled" : "Disabled";
        string id = setting switch
        {
            GlobalNetworkSetting.TaskOffload => "power.network.task-offload.disable",
            GlobalNetworkSetting.Teredo => "power.network.transition-tunnels.disable",
            GlobalNetworkSetting.Timestamps => "power.network.timestamps.disable",
            GlobalNetworkSetting.Ecn => "network.ecn-rss.enable",
            _ => throw new ArgumentOutOfRangeException(nameof(setting))
        };
        var gate = new SemaphoreSlim(1, 1);
        return new TweakDefinition
        {
            Id = id, Name = setting switch { GlobalNetworkSetting.TaskOffload => "TCP Task Offload Off", GlobalNetworkSetting.Teredo => "Teredo & ISATAP Off", GlobalNetworkSetting.Ecn => "ECN + RSS Tweaks", _ => "TCP Timestamps Off" },
            Description = setting switch
            {
                GlobalNetworkSetting.TaskOffload => "Disables global TCP/IP task offloading through the documented Windows setting. Packet processing moves back to the CPU and throughput or power use may worsen. Restart Windows or the network adapter for the change to take effect. Restoring preserves whether the original registry setting was absent or explicitly configured.",
                GlobalNetworkSetting.Teredo => "Disables the local persistent Teredo and ISATAP IPv6 transition modes. Native IPv6 remains enabled. Xbox networking, DirectAccess or networks that need these tunnels may stop working. Restore returns each original mode, including Default; organization policies can override the local setting.",
                GlobalNetworkSetting.Ecn => "Enables TCP Explicit Congestion Notification and global receive-side scaling, then verifies both settings. ECN needs compatible peers and network equipment; lower ping is not guaranteed and incompatible paths may have connection issues. Adapter-level RSS support and processor limits are separate. Saves each original global state for restore; existing connections may retain their negotiated options.",
                _ => "Disables TCP timestamps globally without changing receive-window scaling. Timestamp negotiation and PAWS protection are affected; this is not a guaranteed latency improvement. Existing connections may retain negotiated options. Restore returns the original Disabled, Enabled or Allowed mode."
            },
            Category = TweakCategory.Network, RiskLevel = TweakRiskLevel.Medium, RequiresAdministrator = true,
            RequiresRestart = setting == GlobalNetworkSetting.TaskOffload,
            AffectedSettings = keys.Select(key => "global-network:" + key).ToArray(),
            HasSavedOriginal = () => restore.GetBackup(id) != null,
            CurrentStateReader = async () =>
            {
                var current = await Read();
                return current.Values.All(value => value == appliedValue) ? TweakState.Applied : TweakState.NotApplied;
            },
            ApplyAction = () => Change(true), RestoreAction = () => Change(false)
        };
        async Task<Dictionary<GlobalNetworkSetting, string>> Read()
        {
            var values = new Dictionary<GlobalNetworkSetting, string>();
            foreach (var key in keys) { string value = await store.Read(key); WindowsGlobalNetworkStore.Validate(key, value); values.Add(key, value); }
            return values;
        }
        async Task<TweakActionResult> Change(bool apply)
        {
            await gate.WaitAsync();
            try
            {
                var current = await Read();
                string? saved = restore.GetBackup(id);
                Dictionary<GlobalNetworkSetting, string>? original;
                try { original = saved == null ? null : JsonSerializer.Deserialize<Dictionary<GlobalNetworkSetting, string>>(saved); }
                catch (JsonException ex) { throw new IOException("The global network backup is unreadable. No changes made.", ex); }
                if (saved != null && (original == null || original.Count != keys.Length || keys.Any(key => !original.ContainsKey(key))))
                    throw new IOException("The global network backup is invalid. No changes made.");
                if (original != null) foreach (var item in original) WindowsGlobalNetworkStore.Validate(item.Key, item.Value);
                if (!apply && original == null) return TweakActionResult.Fail("No original global network configuration was saved by Elite.");
                if (apply && original == null)
                {
                    restore.SaveBackup(id, JsonSerializer.Serialize(current));
                    original = JsonSerializer.Deserialize<Dictionary<GlobalNetworkSetting, string>>(restore.GetBackup(id) ?? "null");
                    if (original == null || original.Count != current.Count || current.Any(item => !original.TryGetValue(item.Key, out var value) || value != item.Value))
                        throw new IOException("Global network backup verification failed. No changes made.");
                }
                var errors = new List<string>(); int done = 0;
                foreach (var key in keys)
                {
                    try
                    {
                        string now = await store.Read(key); WindowsGlobalNetworkStore.Validate(key, now);
                        if (now != appliedValue && now != original![key]) throw new IOException("The setting changed outside Elite. Original backup retained.");
                        string target = apply ? appliedValue : original![key];
                        if (now != target) await store.Set(key, target);
                        if (await store.Read(key) != target) throw new IOException("Network setting readback failed. Original backup retained.");
                        done++;
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
                    { errors.Add(key + ": " + ex.Message); }
                }
                if (!apply && errors.Count == 0) restore.ClearBackup(id);
                string message = done + " global network " + (done == 1 ? "setting " : "settings ") + (apply ? appliedValue.ToLowerInvariant() + "." : "restored.")
                    + (setting == GlobalNetworkSetting.TaskOffload ? " Restart Windows or the network adapter to take effect." : "");
                return errors.Count == 0 ? TweakActionResult.Ok(message) : TweakActionResult.Fail(message + " " + string.Join(" ", errors));
            }
            finally { gate.Release(); }
        }
    }
}

