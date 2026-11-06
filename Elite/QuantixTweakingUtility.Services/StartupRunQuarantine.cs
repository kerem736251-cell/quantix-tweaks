using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

namespace QuantixTweakingUtility.Services;

public record StartupRunValue(RegistryHive Hive, RegistryView View, string Name, string Command, RegistryValueKind Kind);

public interface IStartupRunQuarantine
{
    IReadOnlyList<StartupRunValue> Entries();
    IReadOnlyList<StartupRunValue> Originals();
    void Quarantine(StartupRunValue entry);
    void Restore(StartupRunValue entry);
}

public sealed class StartupRunQuarantine : IStartupRunQuarantine
{
    private readonly string runPath;
    private readonly string backupPath;
    private readonly object gate = new();
    public StartupRunQuarantine(string runPath = @"Software\Microsoft\Windows\CurrentVersion\Run",
        string backupPath = @"Software\Quantix\Elite\StartupQuarantine")
    {
        this.runPath = runPath;
        this.backupPath = backupPath;
    }

    public IReadOnlyList<StartupRunValue> Entries()
    {
        var result = new List<StartupRunValue>();
        foreach (var (hive, view) in Locations())
        {
            using var root = RegistryKey.OpenBaseKey(hive, view);
            using var run = root.OpenSubKey(runPath);
            if (run == null) continue;
            foreach (string name in run.GetValueNames().Where(name => name.Length > 0))
            {
                var kind = run.GetValueKind(name);
                if (kind is not (RegistryValueKind.String or RegistryValueKind.ExpandString)) continue;
                var entry = ReadRun(run, hive, view, name);
                if (entry != null) result.Add(entry);
            }
        }
        return result;
    }

    public IReadOnlyList<StartupRunValue> Originals()
    {
        var result = new List<StartupRunValue>();
        foreach (var (hive, view) in Locations())
        {
            using var root = RegistryKey.OpenBaseKey(hive, view);
            using var backups = root.OpenSubKey(backupPath);
            if (backups == null) continue;
            foreach (string id in backups.GetSubKeyNames())
            {
                using var saved = backups.OpenSubKey(id);
                var original = ReadBackup(saved, hive, view);
                if (original == null) continue;
                if (Identity(original) != id) throw new IOException("A startup quarantine record has an invalid identity. Backup retained.");
                result.Add(original);
            }
        }
        return result;
    }

    public void Quarantine(StartupRunValue entry)
    {
        Validate(entry);
        lock (gate)
        {
            using var root = RegistryKey.OpenBaseKey(entry.Hive, entry.View);
            using var parent = root.CreateSubKey(backupPath, true);
            string id = Identity(entry);
            using var saved = parent.CreateSubKey(id, true);
            var original = ReadBackup(saved, entry.Hive, entry.View);
            if (original != null && original != entry) throw new IOException("This startup entry differs from its saved original. No entry removed.");
            using var run = root.OpenSubKey(runPath, true);
            var current = run == null ? null : ReadRun(run, entry.Hive, entry.View, entry.Name);
            if (current == null)
            {
                if (original != null) return;
                throw new IOException("The startup entry is no longer present. No entry removed.");
            }
            if (current != entry) throw new IOException("The startup entry changed during review. No entry removed.");
            if (original == null)
            {
                saved.SetValue("Complete", 0, RegistryValueKind.DWord);
                saved.SetValue("Name", entry.Name, RegistryValueKind.String);
                saved.SetValue("Command", entry.Command, RegistryValueKind.String);
                saved.SetValue("Kind", (int)entry.Kind, RegistryValueKind.DWord);
                saved.SetValue("View", (int)entry.View, RegistryValueKind.DWord);
                saved.SetValue("Digest", Digest(entry), RegistryValueKind.String);
                saved.SetValue("Complete", 1, RegistryValueKind.DWord);
                saved.Flush();
                if (ReadBackup(saved, entry.Hive, entry.View) != entry) throw new IOException("Startup backup verification failed. No entry removed.");
            }
            if (ReadRun(run!, entry.Hive, entry.View, entry.Name) != entry) throw new IOException("The startup entry changed before removal. Backup retained.");
            run!.DeleteValue(entry.Name, false);
            if (ReadRun(run, entry.Hive, entry.View, entry.Name) != null) throw new IOException("Startup quarantine readback failed. Backup retained.");
        }
    }

    public void Restore(StartupRunValue entry)
    {
        Validate(entry);
        lock (gate)
        {
            using var root = RegistryKey.OpenBaseKey(entry.Hive, entry.View);
            using var parent = root.OpenSubKey(backupPath, true);
            if (parent == null) throw new IOException("No saved startup original exists.");
            string id = Identity(entry);
            StartupRunValue? original;
            using (var saved = parent.OpenSubKey(id)) original = ReadBackup(saved, entry.Hive, entry.View);
            if (original == null || original != entry) throw new IOException("The saved startup original does not match the requested entry.");
            using var run = root.CreateSubKey(runPath, true);
            var current = ReadRun(run, entry.Hive, entry.View, entry.Name);
            if (current != null && current != original) throw new IOException("A different startup command now uses this name. Original backup retained.");
            if (current == null) run.SetValue(original.Name, original.Command, original.Kind);
            if (ReadRun(run, entry.Hive, entry.View, entry.Name) != original) throw new IOException("Startup restore verification failed. Original backup retained.");
            parent.DeleteSubKey(id, false);
            parent.Flush();
        }
    }

    private static StartupRunValue? ReadRun(RegistryKey key, RegistryHive hive, RegistryView view, string name)
    {
        if (!key.GetValueNames().Contains(name, StringComparer.OrdinalIgnoreCase)) return null;
        var kind = key.GetValueKind(name);
        if (kind is not (RegistryValueKind.String or RegistryValueKind.ExpandString)) throw new IOException("The startup entry has an unsupported registry type.");
        string command = key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string
            ?? throw new IOException("Cannot read the startup command.");
        return new(hive, view, name, command, kind);
    }
    private static StartupRunValue? ReadBackup(RegistryKey? key, RegistryHive hive, RegistryView view)
    {
        if (key == null || key.GetValue("Complete") is not int complete || complete != 1) return null;
        if (key.GetValue("View") is not int storedView || storedView != (int)view)
            throw new IOException("A startup quarantine record has an invalid registry view. Backup retained.");
        if (key.GetValue("Name") is not string name || key.GetValue("Command", null, RegistryValueOptions.DoNotExpandEnvironmentNames) is not string command
            || key.GetValue("Kind") is not int kind || key.GetValue("Digest") is not string digest)
            throw new IOException("A startup quarantine record is incomplete. Backup retained.");
        var entry = new StartupRunValue(hive, view, name, command, (RegistryValueKind)kind);
        Validate(entry);
        if (Digest(entry) != digest) throw new IOException("Startup quarantine data verification failed. Backup retained.");
        return entry;
    }
    private static string Identity(StartupRunValue entry) => Convert.ToHexString(SHA256.HashData(Encoding.Unicode.GetBytes((int)entry.View + "\0" + entry.Name.ToUpperInvariant())));
    private static string Digest(StartupRunValue entry) => Convert.ToHexString(SHA256.HashData(Encoding.Unicode.GetBytes(entry.Name + "\0" + (int)entry.Kind + "\0" + entry.Command)));
    private static void Validate(StartupRunValue entry)
    {
        if (entry.Name.Length == 0 || entry.Name.Contains('\0') || entry.Kind is not (RegistryValueKind.String or RegistryValueKind.ExpandString)
            || !Locations().Contains((entry.Hive, entry.View))) throw new IOException("Invalid startup quarantine target.");
    }
    private static IEnumerable<(RegistryHive Hive, RegistryView View)> Locations()
    {
        var native = Environment.Is64BitOperatingSystem ? RegistryView.Registry64 : RegistryView.Registry32;
        // HKCU Software is shared between WOW64 views on supported Windows versions.
        yield return (RegistryHive.CurrentUser, native);
        yield return (RegistryHive.LocalMachine, native);
        if (Environment.Is64BitOperatingSystem) yield return (RegistryHive.LocalMachine, RegistryView.Registry32);
    }
}
