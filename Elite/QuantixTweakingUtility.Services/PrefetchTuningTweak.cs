using Microsoft.Win32;
using QuantixTweakingUtility.Models;

namespace QuantixTweakingUtility.Services;

public static class PrefetchTuningTweak
{
    public const string RegistryPath = @"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management\PrefetchParameters";
    public static TweakDefinition Build(IRestoreService restore, RegistryKey? hive = null, string? path = null) =>
        RegistryDwordTweaks.Build("windows.prefetch.boot-only", "Prefetch Tuning",
            "Requests boot-only prefetching by setting EnablePrefetcher to 2. Intended for SSD/NVMe systems; application prefetching is disabled and launches may become slower. Windows versions and SysMain configuration determine actual prefetch behavior; no FPS or boot-time gain is guaranteed. Saves the original DWORD or absence for restore. Restart after changing.",
            TweakCategory.Windows, TweakRiskLevel.Low, hive ?? Registry.LocalMachine,
            "HKLM\\PrefetchParameters:EnablePrefetcher", path ?? RegistryPath, "EnablePrefetcher", 2, restore,
            administrator: true, restart: true);
}
