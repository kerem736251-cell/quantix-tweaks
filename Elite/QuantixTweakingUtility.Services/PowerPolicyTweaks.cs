using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using QuantixTweakingUtility.Models;

namespace QuantixTweakingUtility.Services;

public interface IPowerPolicyStore
{
    Guid ActiveScheme();
    uint Read(Guid scheme, Guid subgroup, Guid setting);
    void Write(Guid scheme, Guid subgroup, Guid setting, uint value);
    void Refresh(Guid scheme);
}

public interface IDualPowerPolicyStore : IPowerPolicyStore
{
    uint ReadDC(Guid scheme, Guid subgroup, Guid setting);
    void WriteDC(Guid scheme, Guid subgroup, Guid setting, uint value);
}

public sealed class WindowsPowerPolicyStore : IDualPowerPolicyStore
{
    public Guid ActiveScheme()
    {
        Check(PowerGetActiveScheme(IntPtr.Zero, out var pointer));
        try { return Marshal.PtrToStructure<Guid>(pointer); }
        finally { LocalFree(pointer); }
    }
    public uint Read(Guid scheme, Guid subgroup, Guid setting)
    {
        Check(PowerReadACValueIndex(IntPtr.Zero, ref scheme, ref subgroup, ref setting, out var value));
        return value;
    }
    public void Write(Guid scheme, Guid subgroup, Guid setting, uint value) =>
        Check(PowerWriteACValueIndex(IntPtr.Zero, ref scheme, ref subgroup, ref setting, value));
    public uint ReadDC(Guid scheme, Guid subgroup, Guid setting)
    {
        Check(PowerReadDCValueIndex(IntPtr.Zero, ref scheme, ref subgroup, ref setting, out var value));
        return value;
    }
    public void WriteDC(Guid scheme, Guid subgroup, Guid setting, uint value) =>
        Check(PowerWriteDCValueIndex(IntPtr.Zero, ref scheme, ref subgroup, ref setting, value));
    public void Refresh(Guid scheme)
    {
        if (ActiveScheme() != scheme) throw new IOException("The active power plan changed. Select the original plan and retry.");
        Check(PowerSetActiveScheme(IntPtr.Zero, ref scheme));
    }
    private static void Check(uint result) { if (result != 0) throw new Win32Exception((int)result); }
    [DllImport("powrprof.dll")] private static extern uint PowerGetActiveScheme(IntPtr root, out IntPtr scheme);
    [DllImport("powrprof.dll")] private static extern uint PowerReadACValueIndex(IntPtr root, ref Guid scheme, ref Guid subgroup, ref Guid setting, out uint value);
    [DllImport("powrprof.dll")] private static extern uint PowerWriteACValueIndex(IntPtr root, ref Guid scheme, ref Guid subgroup, ref Guid setting, uint value);
    [DllImport("powrprof.dll")] private static extern uint PowerReadDCValueIndex(IntPtr root, ref Guid scheme, ref Guid subgroup, ref Guid setting, out uint value);
    [DllImport("powrprof.dll")] private static extern uint PowerWriteDCValueIndex(IntPtr root, ref Guid scheme, ref Guid subgroup, ref Guid setting, uint value);
    [DllImport("powrprof.dll")] private static extern uint PowerSetActiveScheme(IntPtr root, ref Guid scheme);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr pointer);
}

public static class PowerPolicyTweaks
{
    private sealed record Original(Guid Scheme, uint Value);
    private static readonly object Gate = new();
    private static readonly Guid Processor = new("54533251-82be-4824-96c1-47b60b740d00");

    public static IEnumerable<TweakDefinition> Build(IRestoreService restore, IPowerPolicyStore? store = null)
    {
        store ??= new WindowsPowerPolicyStore();
        yield return Make("cpu-epp", "CPU Performance Preference", "Requests maximum CPU performance preference (EPP 0) in the current plan while plugged in. Hardware and firmware determine the response. May increase heat and power use.", Processor, new("36687f9e-e3a5-4dbf-b1dc-15eb381c6863"), 0, TweakRiskLevel.Medium);
        yield return Make("cpu-boost", "CPU Boost Mode Aggressive", "Sets the current plan's plugged-in processor boost mode to Aggressive (2). Behavior depends on the CPU's performance-control interface. May increase temperature and power draw.", Processor, new("be337238-0d82-4146-a960-4f3749d470c7"), 2, TweakRiskLevel.Medium);
        yield return Make("cpu-ramp", "CPU Ramp Rocket", "Sets the current plan's plugged-in processor performance increase policy to Rocket (2). Supported legacy performance-state controllers can request the maximum state more quickly; autonomous hardware may ignore this policy.", Processor, new("465e1f50-b610-473a-ab58-00d1077dc418"), 2, TweakRiskLevel.Medium);
        yield return Make("cpu-idle", "Disable CPU Idle States", "Disables processor idle states in the current plan while plugged in. Can substantially increase idle temperature, fan noise and power consumption. Compare your workload before keeping this setting.", Processor, new("5d76a2ca-e8c0-402f-a133-2158492d58ad"), 1, TweakRiskLevel.High);
        yield return Make("adaptive-brightness", "Adaptive Brightness Off", "Disables ambient-light adaptive brightness in the current plan while plugged in. Requires a supported display sensor. Battery settings remain unchanged.", new("7516b95f-f776-4464-8c53-06167f40cc99"), new("fbd9aa66-9553-4097-ba44-ed6e9d65eab8"), 0, TweakRiskLevel.Low);

        TweakDefinition Make(string suffix, string name, string detail, Guid subgroup, Guid setting, uint target, TweakRiskLevel risk)
        {
            var id = "power.policy." + suffix;
            return new TweakDefinition
            {
                Id = id, Name = name, Description = detail + " Elite saves the original value and restores it only in the same power plan.",
                Category = TweakCategory.Cpu, RiskLevel = risk, RequiresAdministrator = true,
                AffectedSettings = new[] { $"powercfg:AC:{subgroup}:{setting}" },
                CurrentStateReader = () =>
                {
                    try { return Task.FromResult(store.Read(store.ActiveScheme(), subgroup, setting) == target ? TweakState.Applied : TweakState.NotApplied); }
                    catch (Win32Exception) { return Task.FromResult(TweakState.Unsupported); }
                },
                ApplyAction = () => Change(true), RestoreAction = () => Change(false)
            };

            Task<TweakActionResult> Change(bool apply)
            {
                lock (Gate)
                {
                    var scheme = store.ActiveScheme();
                    var current = store.Read(scheme, subgroup, setting);
                    var backup = restore.GetBackup(id);
                    var original = backup == null ? null : JsonSerializer.Deserialize<Original>(backup)
                        ?? throw new IOException("Invalid saved power setting.");
                    if (original != null && original.Scheme != scheme)
                        throw new IOException("This setting has a saved original in a different power plan. Activate that plan before restoring or reapplying.");
                    if (original != null && current != target && current != original.Value)
                        throw new IOException("This power setting changed outside Elite. Its original backup has been retained.");
                    if (!apply && original == null) return Task.FromResult(TweakActionResult.Fail("No original value was saved by Elite."));
                    if (apply && original == null) restore.SaveBackup(id, JsonSerializer.Serialize(new Original(scheme, current)));
                    if (store.ActiveScheme() != scheme) throw new IOException("The active power plan changed. Retry the operation.");
                    var value = apply ? target : original!.Value;
                    store.Write(scheme, subgroup, setting, value);
                    store.Refresh(scheme);
                    if (store.Read(scheme, subgroup, setting) != value) throw new IOException("Power setting verification failed. The original backup has been retained.");
                    if (!apply) restore.ClearBackup(id);
                    return Task.FromResult(TweakActionResult.Ok(name + (apply ? " applied and verified." : " restored and verified.")));
                }
            }
        }
    }
}
