using Microsoft.Win32;
using QuantixTweakingUtility.Models;

namespace QuantixTweakingUtility.Services;

public static class ProtectionControlTweaks
{
    public static TweakDefinition Uac(IRestoreService restore, RegistryKey? hive = null,
        string path = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System") => RegistryDwordTweaks.Build(
            "windows.uac.disable", "Disable UAC",
            "Disables User Account Control and Admin Approval Mode by setting EnableLUA to 0. After restarting, administrator applications can run with full administrator rights without UAC's elevation boundary or consent prompt. This materially weakens Windows security and can affect application compatibility; it is not an FPS optimization. Standard accounts do not become administrators. Elite saves the exact original DWORD or its absence for restore. Restart Windows after applying or restoring.",
            TweakCategory.Windows, TweakRiskLevel.High, hive ?? Registry.LocalMachine,
            @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System:EnableLUA", path, "EnableLUA", 0, restore,
            administrator: true, restart: true);

    public static TweakDefinition Tdr(IRestoreService restore, RegistryKey? hive = null,
        string path = @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers") => RegistryDwordTweaks.Build(
            "windows.gpu-tdr.disable", "Disable GPU Timeout Detection (Tdr)",
            "Disables Windows GPU timeout detection by setting TdrLevel to 0. A hung GPU may leave the desktop frozen instead of allowing Windows to recover the display driver; a forced restart may be required. Microsoft documents this setting for targeted driver-development testing/debugging, not normal end-user performance tuning. It does not fix driver faults or guarantee better performance. Elite preserves the exact original DWORD or its absence. Restart Windows after applying or restoring.",
            TweakCategory.Storage, TweakRiskLevel.High, hive ?? Registry.LocalMachine,
            @"HKLM\SYSTEM\CurrentControlSet\Control\GraphicsDrivers:TdrLevel", path, "TdrLevel", 0, restore,
            administrator: true, restart: true);
}
