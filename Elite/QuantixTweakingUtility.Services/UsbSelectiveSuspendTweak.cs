using System;
using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using QuantixTweakingUtility.Models;

namespace QuantixTweakingUtility.Services;

public static class UsbSelectiveSuspendTweak
{
    public static readonly Guid Group = new("2a737441-1930-4402-8d77-b2bebba308a3");
    public static readonly Guid Setting = new("48e6b7a6-50f5-4782-a5d4-53bb8f07e226");
    private const string Id = "perf.usb-selective-suspend";
    private sealed record Original(int Version, Guid Scheme, uint AC, uint DC);
    public static TweakDefinition Build(IRestoreService restore, IDualPowerPolicyStore? store = null)
    {
        store ??= new WindowsPowerPolicyStore();
        var gate = new object();
        return new TweakDefinition
        {
            Id = Id, Name = "Disable USB Selective Suspend", Category = TweakCategory.Input,
            Description = "Disables USB selective suspend for the current power plan on AC and battery. Idle USB devices stay powered, which can increase battery drain. Elite saves AC and battery values independently and restores them only in the original plan. This does not change per-device wake permissions or guarantee lower input latency.",
            RiskLevel = TweakRiskLevel.Medium, RequiresAdministrator = true,
            AffectedSettings = new[] { "powercfg:usb-selective-suspend" },
            CurrentStateReader = () =>
            {
                try
                {
                    var scheme = store.ActiveScheme();
                    return Task.FromResult(store.Read(scheme, Group, Setting) == 0 && store.ReadDC(scheme, Group, Setting) == 0 ? TweakState.Applied : TweakState.NotApplied);
                }
                catch (Win32Exception) { return Task.FromResult(TweakState.Unsupported); }
            },
            ApplyAction = () => Change(true), RestoreAction = () => Change(false)
        };
        Task<TweakActionResult> Change(bool apply)
        {
            lock (gate)
            {
                var scheme = store.ActiveScheme();
                uint ac = store.Read(scheme, Group, Setting), dc = store.ReadDC(scheme, Group, Setting);
                if (ac > 1 || dc > 1) throw new IOException("USB selective suspend has an unsupported value.");
                string? saved = restore.GetBackup(Id);
                if (saved is "0" or "1")
                    return Task.FromResult(TweakActionResult.Fail("This legacy USB backup recorded only one value and no power-plan identity. Its battery original is unknown. No values changed; the legacy backup is retained for manual recovery in Windows Power Options."));
                Original? original;
                try { original = saved == null ? null : JsonSerializer.Deserialize<Original>(saved); }
                catch (JsonException ex) { throw new IOException("The USB power backup is unreadable. No values changed.", ex); }
                if (saved != null && (original == null || original.Version != 2 || original.Scheme == Guid.Empty || original.AC > 1 || original.DC > 1))
                    throw new IOException("The USB power backup is invalid. No values changed.");
                if (original != null && original.Scheme != scheme) throw new IOException("Activate the original power plan before restoring or reapplying USB selective suspend. Backup retained.");
                if (original != null && ((ac != 0 && ac != original.AC) || (dc != 0 && dc != original.DC)))
                    throw new IOException("A USB power value changed outside Elite. Original backup retained.");
                if (!apply && original == null) return Task.FromResult(TweakActionResult.Fail("No original USB power values were saved by Elite."));
                if (apply && original == null)
                {
                    original = new Original(2, scheme, ac, dc);
                    restore.SaveBackup(Id, JsonSerializer.Serialize(original));
                    if (JsonSerializer.Deserialize<Original>(restore.GetBackup(Id) ?? "null") != original)
                        throw new IOException("USB power backup verification failed. No values changed.");
                }
                if (store.ActiveScheme() != scheme) throw new IOException("The active power plan changed. Original backup retained.");
                uint targetAC = apply ? 0 : original!.AC, targetDC = apply ? 0 : original!.DC;
                if (ac != targetAC) store.Write(scheme, Group, Setting, targetAC);
                if (dc != targetDC) store.WriteDC(scheme, Group, Setting, targetDC);
                store.Refresh(scheme);
                if (store.Read(scheme, Group, Setting) != targetAC || store.ReadDC(scheme, Group, Setting) != targetDC)
                    throw new IOException("USB AC/battery readback failed. Original backup retained.");
                if (!apply) restore.ClearBackup(Id);
                return Task.FromResult(TweakActionResult.Ok(apply ? "USB selective suspend disabled and verified on AC and battery." : "Original AC and battery USB power values restored and verified."));
            }
        }
    }
}
