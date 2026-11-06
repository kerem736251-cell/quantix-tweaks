using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Win32;
using QuantixTweakingUtility.Models;

namespace QuantixTweakingUtility.Services;

public static class KernelMemoryTweak
{
    public const string Path = @"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management";
    public static TweakDefinition Build(IRestoreService restore, RegistryKey? hive = null, string? path = null)
    {
        hive ??= Registry.LocalMachine; path ??= Path;
        const string id = "windows.kernel-memory-policy";
        string[] names = { "DisablePagingExecutive", "LargeSystemCache" };
        var gate = new object();
        return new TweakDefinition
        {
            Id = id, Name = "Kernel", Category = TweakCategory.Windows,
            RiskLevel = TweakRiskLevel.High, RequiresAdministrator = true, RequiresRestart = true,
            Description = "Requests nonpaged executive code and a larger system-cache policy through DisablePagingExecutive=1 and LargeSystemCache=1. This does not lock every driver or kernel allocation in RAM. It may leave less RAM for games and worsen paging or stutter; no performance gain is guaranteed. Saves both original DWORDs or their absence. Restart after applying or restoring.",
            AffectedSettings = names.Select(name => "HKLM\\Memory Management:" + name).ToArray(),
            HasSavedOriginal = () => restore.GetBackup(id) != null,
            CurrentStateReader = () => { lock (gate) { return Task.FromResult(Read().Values.All(value => value == 1) ? TweakState.Applied : TweakState.NotApplied); } },
            ApplyAction = () => Change(true), RestoreAction = () => Change(false)
        };
        Dictionary<string, int?> Read()
        {
            using var key = hive.OpenSubKey(path);
            var values = new Dictionary<string, int?>();
            foreach (string name in names)
            {
                if (key == null || !key.GetValueNames().Contains(name, StringComparer.OrdinalIgnoreCase)) { values.Add(name, null); continue; }
                if (key.GetValueKind(name) != RegistryValueKind.DWord || key.GetValue(name) is not int value)
                    throw new IOException("Kernel policy has an unsupported registry type; no original was discarded.");
                values.Add(name, value);
            }
            return values;
        }
        Task<TweakActionResult> Change(bool apply)
        {
            lock (gate)
            {
                string? raw = restore.GetBackup(id);
                Dictionary<string, int?>? original;
                try { original = raw == null ? null : JsonSerializer.Deserialize<Dictionary<string, int?>>(raw); }
                catch (JsonException ex) { throw new IOException("Saved kernel originals are invalid; retained for review.", ex); }
                if (raw != null && (original == null || original.Count != names.Length || names.Any(name => !original.ContainsKey(name))))
                    throw new IOException("Saved kernel originals are incomplete; retained for review.");
                if (!apply && original == null) return Task.FromResult(TweakActionResult.Fail("No original kernel policy was saved."));
                var current = Read();
                if (original == null)
                {
                    original = current; raw = JsonSerializer.Serialize(original); restore.SaveBackup(id, raw);
                    if (restore.GetBackup(id) != raw) throw new IOException("Cannot verify kernel originals; no policy changed.");
                }
                foreach (string name in names)
                {
                    var now = Read();
                    if (names.Any(item => now[item] != original[item] && now[item] != 1))
                        throw new IOException("Kernel policy changed outside Elite; originals retained.");
                    int? desired = apply ? 1 : original[name];
                    if (now[name] == desired) continue;
                    if (desired.HasValue)
                    {
                        using var key = hive.CreateSubKey(path, true);
                        key.SetValue(name, desired.Value, RegistryValueKind.DWord); key.Flush();
                    }
                    else
                    {
                        using var key = hive.OpenSubKey(path, true); key?.DeleteValue(name, false); key?.Flush();
                    }
                    if (Read()[name] != desired) throw new IOException("Kernel policy readback failed; originals retained.");
                }
                var final = Read();
                if (names.Any(name => final[name] != (apply ? 1 : original[name])))
                    throw new IOException("Kernel policy final verification failed; originals retained.");
                if (!apply) restore.ClearBackup(id);
                return Task.FromResult(TweakActionResult.Ok("Kernel registry policy " + (apply ? "configured" : "restored") + " and verified. Restart Windows; actual RAM residency is not measured."));
            }
        }
    }
}
