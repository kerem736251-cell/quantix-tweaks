using Microsoft.Win32;
using QuantixTweakingUtility.Models;

namespace QuantixTweakingUtility.Services;

public static class InputQueueTweaks
{
    public static TweakDefinition Build(bool mouse, IRestoreService restore, RegistryKey? hive = null, string? path = null)
    {
        string driver=mouse?"mouclass":"kbdclass",value=mouse?"MouseDataQueueSize":"KeyboardDataQueueSize";
        string targetPath=path ?? @"SYSTEM\CurrentControlSet\Services\"+driver+@"\Parameters";
        return RegistryDwordTweaks.Build(mouse?"input.mouse-queue.200":"input.keyboard-queue.200",mouse?"Mouse Data Queue Size":"Data Queue Size",
            "Requests a 200-event "+(mouse?"mouse":"keyboard")+" class-driver input queue, twice the historical 100-event default. This is a fixed target, not a repeated doubling of an existing custom value. A larger queue uses additional nonpaged memory and may help only when the driver buffer overflows; it does not change USB polling rate, hardware rollover or directly guarantee lower input latency. Modern driver behavior can vary. Elite saves the exact original DWORD or absence and restores it without inventing a default. Restart Windows after applying or restoring.",
            TweakCategory.Input,TweakRiskLevel.Medium,hive ?? Registry.LocalMachine,"HKLM\\"+targetPath+":"+value,targetPath,value,200,restore,
            administrator:true,restart:true);
    }
}
