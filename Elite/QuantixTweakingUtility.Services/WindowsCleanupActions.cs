using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using QuantixTweakingUtility.Models;

namespace QuantixTweakingUtility.Services;

public interface IRecycleBinCleaner { Task Empty(); }
public sealed class WindowsRecycleBinCleaner : IRecycleBinCleaner
{
    public Task Empty() => Task.Run(() =>
    {
        Marshal.ThrowExceptionForHR(CoInitializeEx(IntPtr.Zero,0));
        try { Marshal.ThrowExceptionForHR(SHEmptyRecycleBin(IntPtr.Zero,null,7)); }
        finally { CoUninitialize(); }
    });
    [DllImport("ole32.dll")] private static extern int CoInitializeEx(IntPtr reserved,uint mode);
    [DllImport("ole32.dll")] private static extern void CoUninitialize();
    [DllImport("shell32.dll",EntryPoint="SHEmptyRecycleBinW",CharSet=CharSet.Unicode)]
    private static extern int SHEmptyRecycleBin(IntPtr owner,string? root,uint flags);
}

public static class WindowsCleanupActions
{
    public static TweakDefinition Cache(bool shaders, ICleanupService cleanup) => new()
    {
        Id = shaders ? "windows.cleanup.shaders" : "windows.cleanup.temp",
        Name = shaders ? "Clear Shader Caches" : "Clean temp all files", Category = TweakCategory.Windows,
        Description = shaders
            ? "Deletes files in known DirectX, NVIDIA and AMD shader-cache folders. Locked files, directory links and inaccessible locations are skipped. Games rebuild shaders on later launches, which can temporarily increase loading and stutter. This is a one-time cleanup; deleted cache files cannot be restored by Elite."
            : "Deletes accessible files in your local user Temp and Windows Temp folders. Locked files and directory links are skipped. Close installers and apps first; temporary work still needed by an application can be lost. This is a one-time cleanup and deleted files cannot be restored by Elite.",
        RiskLevel = TweakRiskLevel.Medium, RequiresAdministrator = !shaders, IsOneTimeAction = true,
        AffectedSettings = new[] { shaders ? "action:cleanup:shader-caches" : "action:cleanup:user-windows-temp" },
        CurrentStateReader = () => Task.FromResult(TweakState.NotApplied),
        RestoreAction = () => Task.FromResult(TweakActionResult.Fail("Deleted cache files cannot be restored. Applications rebuild caches when needed.")),
        ApplyAction = async () =>
        {
            var result = await cleanup.CleanAsync(shaders ? new[] { "shader-caches" } : new[] { "user-temp", "windows-temp" });
            return result.Success ? TweakActionResult.Ok(result.Message) : TweakActionResult.Fail(result.Message);
        }
    };
    public static TweakDefinition RecycleBin(IRecycleBinCleaner? cleaner = null)
    {
        cleaner ??= new WindowsRecycleBinCleaner();
        return new TweakDefinition
        {
            Id = "windows.cleanup.recycle-bin", Name = "Wipe Recycle Bin", Category = TweakCategory.Windows,
            Description = "Permanently empties your Recycle Bin across connected drives using the Windows shell. Review its contents first. This operation is irreversible and Elite cannot restore the deleted files.",
            RiskLevel = TweakRiskLevel.High, IsOneTimeAction = true,
            AffectedSettings = new[] { "action:cleanup:recycle-bin" },
            CurrentStateReader = () => Task.FromResult(TweakState.NotApplied),
            RestoreAction = () => Task.FromResult(TweakActionResult.Fail("Permanently deleted Recycle Bin contents cannot be restored by Elite.")),
            ApplyAction = async () => { await cleaner.Empty(); return TweakActionResult.Ok("Windows emptied the Recycle Bin. Deleted contents cannot be restored by Elite."); }
        };
    }
}
