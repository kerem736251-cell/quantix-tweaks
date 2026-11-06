using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Win32;
using QuantixTweakingUtility.Models;

namespace QuantixTweakingUtility.Services;

public static class MmcssSchedulingTweaks
{
    public const string Root = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks";
    public static TweakDefinition AudioWindow(IRestoreService restore, RegistryKey? hive = null, string root = Root) => Build(
        "windows.mmcss.audio-window", "Audio + Window Manager Scheduling", new[] { "Audio", "Window Manager" }, restore, hive ?? Registry.LocalMachine, root, false);
    public static TweakDefinition Games(IRestoreService restore, RegistryKey? hive = null, string root = Root) => Build(
        "gaming.mmcss-priority.boost", "Boost Games Task Scheduling Priority", new[] { "Games" }, restore, hive ?? Registry.LocalMachine, root, true);

    private static TweakDefinition Build(string id, string name, string[] tasks, IRestoreService restore, RegistryKey hive, string root, bool legacy)
    {
        var gate = new object();
        var card = RegistryStringTweaks.Build(id, name,
            "Sets the " + string.Join(" and ", tasks) + " MMCSS tasks' Scheduling Category to High. Only threads registered with these multimedia tasks receive MMCSS scheduling; this does not change an entire process to the Real-time priority class. High is designed for time-sensitive pro-audio work and can reduce CPU time available to other work. It does not guarantee fewer crackles or better frame pacing. Elite saves each original string or its absence before applying. Restart Windows so registered tasks pick up the change.",
            TweakCategory.Windows, TweakRiskLevel.High,
            tasks.Select(task => new RegistryStringSetting(hive, "HKLM\\MMCSS\\Tasks\\" + task + ":Scheduling Category", root + "\\" + task, "Scheduling Category", "High")).ToArray(),
            restore, administrator: true, restart: true);
        var read = card.CurrentStateReader; var apply = card.ApplyAction; var revert = card.RestoreAction;
        card.HasSavedOriginal = () => restore.GetBackup(id) != null;
        card.CurrentStateReader = () => Available() ? read() : Task.FromResult(TweakState.Unsupported);
        card.ApplyAction = () => Change(apply);
        card.RestoreAction = () => Change(revert);
        return card;

        bool Available() => tasks.All(task => { using var key = hive.OpenSubKey(root + "\\" + task); return key != null; });
        Task<TweakActionResult> Change(Func<Task<TweakActionResult>> action)
        {
            lock (gate)
            {
                if (!Available()) return Task.FromResult(TweakActionResult.Fail("The required MMCSS task definitions are unavailable. Saved originals were retained."));
                if (legacy && restore.GetBackup(id) is { } raw && raw is "High" or "Medium" or "Low")
                {
                    // Older Elite stored a single raw category. Preserve that recorded original during migration.
                    string migrated = JsonSerializer.Serialize(new[] { raw });
                    if (restore is not IRestoreBackupMigrator migrator || !migrator.TryMigrateBackup(id, raw, migrated)
                        || restore.GetBackup(id) != migrated) throw new IOException("MMCSS original migration could not be verified; no task setting changed.");
                }
                return action(); // Shared registry implementation is synchronous under its own lock.
            }
        }
    }
}
