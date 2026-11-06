using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;
using QuantixTweakingUtility.Models;

namespace QuantixTweakingUtility.Services;

public record IndexingEntry(string Path, string Identity, bool Excluded);
public record IndexingOriginal(string[] Roots, IndexingEntry[] Entries);
internal sealed class LinkedGameIndexingPathException : IOException
{
    public LinkedGameIndexingPathException() : base("Linked game files are excluded from indexing changes.") { }
}
public interface IGameIndexingStore
{
    string[] Roots();
    IndexingEntry[] Scan(string[] roots);
    IndexingEntry Read(string path);
    void Write(IndexingEntry expected, bool excluded);
}
public sealed class WindowsGameIndexingStore : IGameIndexingStore
{
    public static string[] DefaultRoots() => new[] {
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Epic Games", "Fortnite"),
        Path.Combine(Path.GetPathRoot(Environment.SystemDirectory)!, "Riot Games", "VALORANT", "live"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FiveM", "FiveM.app")
    }.Select(Path.GetFullPath).ToArray();
    public string[] Roots() => DefaultRoots().Where(Directory.Exists).ToArray();
    public IndexingEntry[] Scan(string[] roots)
    {
        var entries = new Dictionary<string, IndexingEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (string root in roots) Visit(root);
        return entries.Values.ToArray();
        void Visit(string path)
        {
            if (entries.Count >= 200000) throw new IOException("Game folder scan exceeds the supported recovery limit; nothing changed.");
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) return;
            IndexingEntry entry;
            try { entry = Read(path); }
            catch (LinkedGameIndexingPathException) { return; }
            entries.Add(entry.Path, entry);
            if (Directory.Exists(path))
            {
                using var pin = CacheFileCleaner.Pin(path);
                foreach (string child in Directory.EnumerateFileSystemEntries(path)) Visit(child);
            }
        }
    }
    public IndexingEntry Read(string path) => WithHandle(path, false, handle => ReadHandle(path, handle));
    public void Write(IndexingEntry expected, bool excluded) => WithHandle(expected.Path, true, handle =>
    {
        var now = ReadHandle(expected.Path, handle);
        if (now.Identity != expected.Identity || now.Excluded != expected.Excluded) throw new IOException("A game file changed before its indexing update; original retained.");
        var attributes = File.GetAttributes(handle);
        File.SetAttributes(handle, excluded ? attributes | FileAttributes.NotContentIndexed : attributes & ~FileAttributes.NotContentIndexed);
        if (ReadHandle(expected.Path, handle).Excluded != excluded) throw new IOException("Game indexing attribute readback failed; original retained.");
        return true;
    });
    private static T WithHandle<T>(string path, bool write, Func<SafeFileHandle, T> action)
    {
        string full = Path.GetFullPath(path);
        if (!Path.IsPathFullyQualified(path) || full.StartsWith(@"\\", StringComparison.Ordinal) || full.TrimEnd('\\') == Path.GetPathRoot(full)!.TrimEnd('\\'))
            throw new IOException("A specific local game path is required.");
        var pins = new List<SafeFileHandle>();
        try
        {
            var ancestors = new Stack<string>();
            for (var directory = Directory.GetParent(full); directory != null; directory = directory.Parent) ancestors.Push(directory.FullName);
            while (ancestors.Count > 0) pins.Add(CacheFileCleaner.Pin(ancestors.Pop()));
            using var handle = CreateFile(full, write ? 0x180u : 0x80u, 3, IntPtr.Zero, 3, 0x02200000, IntPtr.Zero);
            if (handle.IsInvalid) throw new IOException("Cannot open game indexing attributes (Windows error " + Marshal.GetLastWin32Error() + ").");
            return action(handle);
        }
        finally { foreach (var pin in pins) pin.Dispose(); }
    }
    private static IndexingEntry ReadHandle(string path, SafeFileHandle handle)
    {
        if (!GetFileInformationByHandle(handle, out var info)) throw new IOException("Cannot verify game file identity.");
        if ((info.Attributes & (uint)FileAttributes.ReparsePoint) != 0 || (info.Attributes & (uint)FileAttributes.Directory) == 0 && info.Links > 1)
            throw new LinkedGameIndexingPathException();
        return new(Path.GetFullPath(path), $"{info.VolumeSerial:X8}:{info.IndexHigh:X8}{info.IndexLow:X8}", (info.Attributes & (uint)FileAttributes.NotContentIndexed) != 0);
    }
    [StructLayout(LayoutKind.Sequential)] private struct FileInformation
    {
        public uint Attributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME Creation, Access, Write;
        public uint VolumeSerial, SizeHigh, SizeLow, Links, IndexHigh, IndexLow;
    }
    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out FileInformation information);
}

public static class GameIndexingTweak
{
    public static TweakDefinition Build(IRestoreService restore, IGameIndexingStore? store = null)
    {
        store ??= new WindowsGameIndexingStore();
        const string id = "windows.game-content-indexing.disable";
        var gate = new SemaphoreSlim(1, 1);
        return new TweakDefinition
        {
            Id = id, Name = "Exclude Game Folders from Indexing", Category = TweakCategory.Windows,
            RiskLevel = TweakRiskLevel.Medium, RequiresAdministrator = true,
            Description = "Marks files and folders in the default Fortnite, VALORANT and FiveM install locations as not content-indexed. Custom install paths and linked files/folders are excluded. Windows can still index basic properties; third-party indexers may ignore this flag. Saves each original flag and file identity for restore. Changed or inaccessible files retain recovery records. Restore covers recorded files; new files may retain inherited exclusion flags. No files are deleted.",
            AffectedSettings = new[] { "GameFolders:NotContentIndexed" }, HasSavedOriginal = () => restore.GetBackup(id) != null,
            CurrentStateReader = () => Task.Run(() => { var roots = store.Roots(); if (roots.Length == 0) return TweakState.Unsupported; var entries = store.Scan(roots); return entries.Length > 0 && entries.All(entry => entry.Excluded) ? TweakState.Applied : TweakState.NotApplied; }),
            ApplyAction = () => Change(true), RestoreAction = () => Change(false)
        };
        async Task<TweakActionResult> Change(bool apply)
        {
            await gate.WaitAsync();
            try { return await Task.Run(() => Run(apply)); }
            finally { gate.Release(); }
        }
        TweakActionResult Run(bool apply)
        {
            string? raw = restore.GetBackup(id); IndexingOriginal? original;
            try { original = raw == null ? null : JsonSerializer.Deserialize<IndexingOriginal>(raw); }
            catch (JsonException ex) { throw new IOException("Game indexing backup is invalid; retained for review.", ex); }
            if (raw != null && original == null) throw new IOException("Game indexing backup is invalid.");
            if (!apply && original == null) return TweakActionResult.Fail("No original game indexing flags were saved.");
            if (original == null)
            {
                string[] roots = store.Roots();
                if (roots.Length == 0) return TweakActionResult.Fail("No supported default game install folders were found.");
                original = new(roots, store.Scan(roots)); Validate(original);
                raw = JsonSerializer.Serialize(original); restore.SaveBackup(id, raw);
                if (restore.GetBackup(id) != raw) throw new IOException("Cannot verify saved indexing originals; no attributes changed.");
            }
            Validate(original);
            if (store is WindowsGameIndexingStore && original.Roots.Any(root => !WindowsGameIndexingStore.DefaultRoots().Contains(root, StringComparer.OrdinalIgnoreCase)))
                throw new IOException("Saved indexing roots are outside supported game locations; no attributes changed.");
            if (apply)
            {
                var currentEntries = store.Scan(original.Roots);
                var identities = original.Entries.ToDictionary(entry => entry.Path, entry => entry.Identity, StringComparer.OrdinalIgnoreCase);
                if (currentEntries.Length != original.Entries.Length || currentEntries.Any(entry => !identities.TryGetValue(entry.Path, out string? identity) || identity != entry.Identity))
                    throw new IOException("The game folder contents changed. Restore or review the saved snapshot before scanning new files; originals retained.");
            }
            int completed = 0; var failures = new List<string>();
            foreach (var entry in original.Entries)
            {
                try
                {
                    var now = store.Read(entry.Path);
                    if (now.Identity != entry.Identity) throw new IOException("File identity changed.");
                    bool desired = apply || entry.Excluded;
                    if (now.Excluded != desired) store.Write(now, desired);
                    var after = store.Read(entry.Path);
                    if (after.Identity != entry.Identity || after.Excluded != desired) throw new IOException("Readback failed.");
                    completed++;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception) { if (failures.Count < 3) failures.Add(entry.Path + ": " + ex.Message); }
            }
            if (completed != original.Entries.Length) return TweakActionResult.Fail($"Verified {completed}/{original.Entries.Length} indexing flags. Originals retained for retry. " + string.Join(" ", failures));
            foreach (var entry in original.Entries)
            {
                var final = store.Read(entry.Path);
                if (final.Identity != entry.Identity || final.Excluded != (apply || entry.Excluded))
                    throw new IOException("Game indexing changed during final verification; originals retained.");
            }
            if (!apply) restore.ClearBackup(id);
            return TweakActionResult.Ok($"Verified {completed} game indexing flags " + (apply ? "applied." : "restored."));
        }
    }
    private static void Validate(IndexingOriginal original)
    {
        if (original.Roots == null || original.Roots.Length == 0 || original.Entries == null || original.Entries.Length is 0 or > 200000 || original.Roots.Any(root => !Path.IsPathFullyQualified(root) || root.StartsWith(@"\\") || Path.GetFullPath(root).TrimEnd('\\') == Path.GetPathRoot(root)!.TrimEnd('\\')))
            throw new IOException("Game indexing scope is invalid; no attributes changed.");
        if (original.Entries.Any(entry => entry == null || !Path.IsPathFullyQualified(entry.Path) || string.IsNullOrWhiteSpace(entry.Identity) ||
            !original.Roots.Any(root => entry.Path.Equals(root, StringComparison.OrdinalIgnoreCase) || entry.Path.StartsWith(root.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase)) || Path.GetFullPath(entry.Path) != entry.Path) ||
            original.Entries.Select(entry => entry.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() != original.Entries.Length)
            throw new IOException("Game indexing recovery paths are invalid; no attributes changed.");
    }
}
