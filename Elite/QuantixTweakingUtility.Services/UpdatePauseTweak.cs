using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Win32;
using QuantixTweakingUtility.Models;

namespace QuantixTweakingUtility.Services;

public interface IUpdatePauseStore
{
    bool Supported { get; }
    string? Read(string name);
    void Write(string name, string? value);
}

public sealed class WindowsUpdatePauseStore : IUpdatePauseStore
{
    private const string Path = @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate";
    public bool Supported
    {
        get
        {
            string edition = Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion", "EditionID", "") as string ?? "";
            bool policyEdition = edition.StartsWith("Professional", StringComparison.OrdinalIgnoreCase)
                || edition.Contains("Enterprise", StringComparison.OrdinalIgnoreCase) || edition.Contains("Education", StringComparison.OrdinalIgnoreCase);
            bool wsus = Registry.GetValue(@"HKEY_LOCAL_MACHINE\" + Path + @"\AU", "UseWUServer", 0) is int value && value == 1;
            return Environment.OSVersion.Version.Build >= 19041 && policyEdition && !wsus;
        }
    }
    public string? Read(string name)
    {
        ValidateName(name);
        using var key = Registry.LocalMachine.OpenSubKey(Path);
        if (key == null || !key.GetValueNames().Contains(name, StringComparer.OrdinalIgnoreCase)) return null;
        if (key.GetValueKind(name) != RegistryValueKind.String) throw new IOException("The update pause policy has an unexpected registry type. No changes were made.");
        return (string?)key.GetValue(name);
    }
    public void Write(string name, string? value)
    {
        ValidateName(name);
        if (value == null)
        {
            using var key = Registry.LocalMachine.OpenSubKey(Path, true);
            key?.DeleteValue(name, false);
        }
        else
        {
            using var key = Registry.LocalMachine.CreateSubKey(Path, true);
            key.SetValue(name, value, RegistryValueKind.String);
        }
    }
    private static void ValidateName(string name)
    {
        if (!UpdatePauseTweak.Names.Contains(name, StringComparer.Ordinal)) throw new IOException("Unknown update pause policy.");
    }
}

public static class UpdatePauseTweak
{
    internal static readonly string[] Names = { "PauseFeatureUpdatesStartTime", "PauseQualityUpdatesStartTime" };
    private sealed record Original(string TargetDate, Dictionary<string, string?> Values);
    public static TweakDefinition Build(IRestoreService restore, IUpdatePauseStore? store = null, Func<DateTimeOffset>? now = null)
    {
        store ??= new WindowsUpdatePauseStore();
        now ??= () => DateTimeOffset.UtcNow;
        var gate = new object();
        const string id = "windows.update-pause.35days";
        return new TweakDefinition
        {
            Id = id, Name = "Pause Windows Update · 5 Weeks",
            Description = "Configures the documented feature and quality update pause policies for 35 days from today. Quality/security updates are delayed during the pause. Windows handles expiration; other update blockers and organization policies still apply. Requires a supported Pro, Enterprise or Education system using Windows Update, not WSUS. Restoring returns the original policy values.",
            Category = TweakCategory.Windows, RiskLevel = TweakRiskLevel.High, RequiresAdministrator = true,
            AffectedSettings = Names.Select(name => @"HKLM\Policies\WindowsUpdate:" + name).ToArray(),
            CurrentStateReader = () =>
            {
                if (!store.Supported) return Task.FromResult(TweakState.Unsupported);
                try
                {
                    var today = now().UtcDateTime.Date;
                    bool paused = Names.All(name => DateTime.TryParseExact(store.Read(name), "yyyy-MM-dd", CultureInfo.InvariantCulture,
                        DateTimeStyles.None, out var start) && start <= today && today < start.AddDays(35));
                    return Task.FromResult(paused ? TweakState.Applied : TweakState.NotApplied);
                }
                catch (IOException) { return Task.FromResult(TweakState.Unknown); }
            },
            ApplyAction = () => Change(true), RestoreAction = () => Change(false)
        };
        Task<TweakActionResult> Change(bool apply)
        {
            lock (gate)
            {
                if (apply && !store.Supported) return Task.FromResult(TweakActionResult.Fail("This update pause policy requires a supported Windows policy edition without WSUS."));
                var current = Names.ToDictionary(name => name, name => store.Read(name));
                string? json = restore.GetBackup(id);
                var saved = json == null ? null : JsonSerializer.Deserialize<Original>(json) ?? throw new IOException("Invalid update pause backup.");
                if (saved != null)
                {
                    if (saved.Values == null || saved.Values.Count != Names.Length || Names.Any(name => !saved.Values.ContainsKey(name))
                        || !DateTime.TryParseExact(saved.TargetDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                        throw new IOException("Invalid update pause backup. No policies changed.");
                    if (Names.Any(name => current[name] != saved.Values[name] && current[name] != saved.TargetDate))
                        throw new IOException("An update pause policy changed outside Elite. The original backup has been retained.");
                }
                if (!apply && saved == null) return Task.FromResult(TweakActionResult.Fail("No original update pause policy was saved by Elite."));
                if (apply && saved != null)
                {
                    var start = DateTime.ParseExact(saved.TargetDate, "yyyy-MM-dd", CultureInfo.InvariantCulture);
                    var today = now().UtcDateTime.Date;
                    if (start > today || today >= start.AddDays(35))
                        return Task.FromResult(TweakActionResult.Fail("The saved pause is outside its 35-day window. Restore the saved policy before starting a new pause."));
                }
                if (apply && saved == null)
                {
                    saved = new(now().UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), current);
                    restore.SaveBackup(id, JsonSerializer.Serialize(saved));
                }
                // A retry uses its original start date, so it cannot silently extend the pause.
                foreach (string name in Names) store.Write(name, apply ? saved!.TargetDate : saved!.Values[name]);
                if (Names.Any(name => store.Read(name) != (apply ? saved!.TargetDate : saved!.Values[name])))
                    throw new IOException("Update pause policy readback failed. The original backup has been retained.");
                if (!apply) restore.ClearBackup(id);
                string message = apply ? "Update pause policies configured through " + DateTime.ParseExact(saved!.TargetDate, "yyyy-MM-dd", CultureInfo.InvariantCulture).AddDays(35).ToString("dd MMM yyyy", CultureInfo.CurrentCulture) + "."
                    : "Original update pause policy values restored and verified.";
                return Task.FromResult(TweakActionResult.Ok(message));
            }
        }
    }
}
