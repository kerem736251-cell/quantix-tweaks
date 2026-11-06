using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace QuantixTweakingUtility.Services;

public record CacheFileResult(long Bytes, int Files, int Skipped);

public static class CacheFileCleaner
{
    public static CacheFileResult Inspect(string path, string? preserveFile = null) => Walk(path, false, preserveFile);
    public static CacheFileResult Clear(string path, string? preserveFile = null) => Walk(path, true, preserveFile);
    private static CacheFileResult Walk(string path, bool delete, string? preserveFile)
    {
        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        if (!Path.IsPathFullyQualified(path) || root.Equals(Path.TrimEndingDirectorySeparator(Path.GetPathRoot(root)!), StringComparison.OrdinalIgnoreCase)
            || root.StartsWith(@"\\", StringComparison.Ordinal)) throw new IOException("Cleanup requires a specific local cache folder, not a drive or network root.");
        if (!Directory.Exists(root)) return new(0,0,0);
        string? preserve = preserveFile == null ? null : Path.GetFullPath(preserveFile);
        long bytes = 0; int files = 0, skipped = 0;
        var guards = new List<SafeFileHandle>();
        try
        {
            // Pin the complete ancestor chain without delete sharing. A directory cannot be swapped for a junction while its contents are inspected.
            var ancestors = new Stack<string>();
            for (var directory = new DirectoryInfo(root); directory != null; directory = directory.Parent) ancestors.Push(directory.FullName);
            while (ancestors.Count > 0) guards.Add(Pin(ancestors.Pop()));
            Visit(root);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception) { skipped++; }
        finally { foreach (var guard in guards) guard.Dispose(); }
        return new(bytes,files,skipped);

        void Visit(string directory)
        {
            if (!Inside(directory)) throw new IOException("Cleanup traversal escaped its cache folder.");
            string[] entries;
            try { entries = Directory.GetFileSystemEntries(directory); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { skipped++; return; }
            foreach (string entry in entries)
            {
                try
                {
                    string full = Path.GetFullPath(entry);
                    if (!Inside(full)) throw new IOException("Cleanup entry escaped its cache folder.");
                    var attributes = File.GetAttributes(full);
                    if ((attributes & FileAttributes.ReparsePoint) != 0) { skipped++; continue; }
                    if ((attributes & FileAttributes.Directory) != 0)
                    {
                        using (var guard = Pin(full)) Visit(full);
                        if (delete && Directory.GetFileSystemEntries(full).Length == 0) Directory.Delete(full, false);
                    }
                    else if (!full.Equals(preserve, StringComparison.OrdinalIgnoreCase))
                    {
                        long length = new FileInfo(full).Length;
                        if (delete) File.Delete(full);
                        bytes += length; files++;
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception) { skipped++; }
            }
        }
        bool Inside(string candidate) => candidate.Equals(root,StringComparison.OrdinalIgnoreCase) || candidate.StartsWith(root + Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase);
    }
    internal static SafeFileHandle Pin(string directory)
    {
        var handle = CreateFile(directory, 0, 3, IntPtr.Zero, 3, 0x02200000, IntPtr.Zero); // share READ|WRITE; OPEN_REPARSE_POINT|BACKUP_SEMANTICS
        if (handle.IsInvalid) { int error = Marshal.GetLastWin32Error(); handle.Dispose(); throw new Win32Exception(error); }
        if (!GetFileInformationByHandle(handle,out var info)) { int error = Marshal.GetLastWin32Error(); handle.Dispose(); throw new Win32Exception(error); }
        if ((info.Attributes & (uint)FileAttributes.ReparsePoint) != 0 || (info.Attributes & (uint)FileAttributes.Directory) == 0)
        { handle.Dispose(); throw new IOException("Directory links are excluded from cache cleanup."); }
        return handle;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct FileInformation
    {
        public uint Attributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME Creation,Access,Write;
        public uint VolumeSerial, SizeHigh, SizeLow, Links, IndexHigh, IndexLow;
    }
    [DllImport("kernel32.dll",EntryPoint="CreateFileW",CharSet=CharSet.Unicode,SetLastError=true)]
    private static extern SafeFileHandle CreateFile(string name,uint access,uint share,IntPtr security,uint creation,uint flags,IntPtr template);
    [DllImport("kernel32.dll",SetLastError=true)]
    [return:MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle handle,out FileInformation info);
}
