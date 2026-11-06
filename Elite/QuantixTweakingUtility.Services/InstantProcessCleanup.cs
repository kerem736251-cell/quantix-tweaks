using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using QuantixTweakingUtility.Models;

namespace QuantixTweakingUtility.Services;

public record CleanupProcess(int Id, string Name, int Session, long Started, bool VisibleWindow);
public interface ICleanupProcessStore
{
    int Session { get; }
    IReadOnlyList<CleanupProcess> Snapshot(IReadOnlyCollection<string> names);
    Task<bool> Stop(CleanupProcess original, bool backgroundOnly);
}

public sealed class WindowsCleanupProcessStore : ICleanupProcessStore
{
    public int Session { get { using var current = Process.GetCurrentProcess(); return current.SessionId; } }
    public IReadOnlyList<CleanupProcess> Snapshot(IReadOnlyCollection<string> names)
    {
        var result = new List<CleanupProcess>();
        foreach (string name in names)
        {
            foreach (var process in Process.GetProcessesByName(name))
            {
                using (process)
                {
                    try
                    {
                        if (process.SessionId != Session || process.HasExited) continue;
                        result.Add(new(process.Id, process.ProcessName, process.SessionId, process.StartTime.ToUniversalTime().Ticks, process.MainWindowHandle != IntPtr.Zero));
                    }
                    catch (Exception ex) when (ex is Win32Exception or InvalidOperationException) { }
                }
            }
        }
        return result;
    }
    public async Task<bool> Stop(CleanupProcess original, bool backgroundOnly)
    {
        Process process;
        try { process = Process.GetProcessById(original.Id); }
        catch (ArgumentException) { return false; }
        using (process)
        {
            if (process.HasExited) return false;
            if (process.SessionId != Session || process.SessionId != original.Session
                || !process.ProcessName.Equals(original.Name, StringComparison.OrdinalIgnoreCase)
                || process.StartTime.ToUniversalTime().Ticks != original.Started)
                throw new IOException("Process identity changed; it was not stopped.");
            if (backgroundOnly && Snapshot(new[] { original.Name }).Any(item => item.VisibleWindow))
                throw new IOException("The browser now has an open window; it was not stopped.");
            // Stop only the matched process, never an arbitrary descendant tree.
            process.Kill();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try { await process.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException) { throw new IOException("The process did not exit within five seconds."); }
            return true;
        }
    }
}

public static class InstantProcessCleanup
{
    private static readonly string[] Browsers = { "msedge", "chrome", "brave", "opera", "firefox" };
    private static readonly string[] Bloat = { "OneDrive", "Widgets", "WidgetService", "GameBar", "GameBarFTServer", "GameBarPresenceWriter", "PhoneExperienceHost", "YourPhone", "ms-teams", "Teams", "Cortana" };
    public static TweakDefinition Build(bool browsers, ICleanupProcessStore? store = null)
    {
        store ??= new WindowsCleanupProcessStore();
        var gate = new SemaphoreSlim(1, 1);
        var names = browsers ? Browsers : Bloat;
        return new TweakDefinition
        {
            Id = browsers ? "processes.cleanup.browsers" : "processes.cleanup.bloat",
            Name = browsers ? "Kill Background Browsers" : "Kill Bloat Processes Now",
            Description = browsers
                ? "Stops leftover Edge, Chrome, Brave, Opera and Firefox processes in your Windows session when that browser has no open main window. Browser profiles and saved sign-ins remain on disk. Open browser families are skipped. This is a one-time action; extensions may restart processes. Unsaved background work can be lost."
                : "Stops running OneDrive, Widgets, Game Bar, Phone Link, Teams and Cortana processes in your Windows session. Unsaved work, active sync and calls can be interrupted. Nothing is uninstalled and startup settings stay unchanged; Windows or the apps may restart them. This one-time action cannot be undone by Restore.",
            Category = TweakCategory.Windows, RiskLevel = TweakRiskLevel.Medium, IsOneTimeAction = true,
            AffectedSettings = new[] { browsers ? "action:processes:background-browsers" : "action:processes:bloat" },
            CurrentStateReader = () => Task.FromResult(TweakState.NotApplied),
            RestoreAction = () => Task.FromResult(TweakActionResult.Fail("Stopped processes cannot be restored. Reopen the apps you need.")),
            ApplyAction = async () =>
            {
                await gate.WaitAsync();
                try
                {
                    var snapshot = store.Snapshot(names).Where(item => item.Session == store.Session
                        && names.Contains(item.Name, StringComparer.OrdinalIgnoreCase)).ToArray();
                    var open = snapshot.Where(item => item.VisibleWindow).Select(item => item.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
                    var targets = snapshot.Where(item => !browsers || !open.Contains(item.Name)).ToArray();
                    int stopped = 0, gone = 0; var errors = new List<string>();
                    foreach (var process in targets)
                    {
                        try { if (await store.Stop(process, browsers)) stopped++; else gone++; }
                        catch (Exception ex) when (ex is IOException or Win32Exception or InvalidOperationException or UnauthorizedAccessException)
                        { errors.Add(process.Name + ": " + ex.Message); }
                    }
                    string message = stopped + " processes stopped." + (gone > 0 ? " " + gone + " had already exited." : "")
                        + (browsers && open.Count > 0 ? " Browsers with open windows were skipped." : "");
                    return errors.Count == 0 ? TweakActionResult.Ok(message) : TweakActionResult.Fail(message + " " + string.Join(" ", errors));
                }
                finally { gate.Release(); }
            }
        };
    }
}
