using System;
using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using QuantixTweakingUtility.Models;

namespace QuantixTweakingUtility.Services;

public static class WifiPowerTweak
{
    public const string Id = "network.wifi.power.maximum";
    public static readonly Guid Group = new("19cbb8fa-5279-450e-9fac-8a3d5fedd0c1");
    public static readonly Guid Setting = new("12bbebe6-58d6-4636-95bb-3217ef867c1a");
    private sealed record Original(Guid Scheme, uint AC, uint DC);
    public static TweakDefinition Build(IRestoreService restore, IDualPowerPolicyStore? store = null)
    {
        store ??= new WindowsPowerPolicyStore();var gate=new object();
        return new TweakDefinition{
            Id=Id,Name="Max Wireless Performance",Category=TweakCategory.Network,RiskLevel=TweakRiskLevel.Medium,RequiresAdministrator=true,
            Description="Sets the current power plan's Wireless Adapter Power Saving Mode to Maximum Performance on AC and battery. This can increase battery drain. Ethernet and per-driver Wi-Fi options remain unchanged; some modern adapters may ignore the legacy plan policy. Elite saves both original values independently for this power plan.",
            AffectedSettings=new[]{"powercfg:AC:"+Group+":"+Setting,"powercfg:DC:"+Group+":"+Setting},HasSavedOriginal=()=>restore.GetBackup(Id)!=null,
            CurrentStateReader=()=>{
                try{var scheme=store.ActiveScheme();var ac=store.Read(scheme,Group,Setting);var dc=store.ReadDC(scheme,Group,Setting);return Task.FromResult(ac>3||dc>3?TweakState.Unsupported:ac==0&&dc==0?TweakState.Applied:TweakState.NotApplied);}
                catch(Win32Exception){return Task.FromResult(TweakState.Unsupported);}
            },ApplyAction=()=>Change(true),RestoreAction=()=>Change(false)
        };
        Task<TweakActionResult> Change(bool apply)
        {
            lock(gate){
                var scheme=store.ActiveScheme();if(scheme==Guid.Empty)throw new IOException("No valid power plan is active.");
                uint ac=store.Read(scheme,Group,Setting),dc=store.ReadDC(scheme,Group,Setting);
                if(ac>3||dc>3)throw new IOException("The wireless power-saving policy has an unsupported value.");
                string? raw=restore.GetBackup(Id);Original? original;
                try{original=raw==null?null:JsonSerializer.Deserialize<Original>(raw);}
                catch(JsonException ex){throw new IOException("Unreadable wireless power backup; no settings changed.",ex);}
                if(raw!=null&&(original==null||original.Scheme==Guid.Empty||original.AC>3||original.DC>3))throw new IOException("Invalid wireless power backup; no settings changed.");
                if(original!=null&&original.Scheme!=scheme)throw new IOException("Activate the original power plan before restoring wireless power settings. Original backup retained.");
                if(!apply&&original==null)return Task.FromResult(TweakActionResult.Fail("No original wireless power values were saved by Elite."));
                if(original==null){original=new(scheme,ac,dc);raw=JsonSerializer.Serialize(original);restore.SaveBackup(Id,raw);if(restore.GetBackup(Id)!=raw)throw new IOException("Wireless power backup verification failed; no settings changed.");}
                uint targetAC=apply?0:original.AC,targetDC=apply?0:original.DC;
                Guard();if(ac!=targetAC)store.Write(scheme,Group,Setting,targetAC);
                Guard();if(dc!=targetDC)store.WriteDC(scheme,Group,Setting,targetDC);
                Guard();store.Refresh(scheme);
                if(store.Read(scheme,Group,Setting)!=targetAC||store.ReadDC(scheme,Group,Setting)!=targetDC)throw new IOException("Wireless AC/battery readback failed. Original backup retained.");
                if(!apply)restore.ClearBackup(Id);
                return Task.FromResult(TweakActionResult.Ok(apply?"Maximum wireless performance requested and verified on AC and battery.":"Original AC and battery wireless power settings restored and verified."));
                void Guard(){
                    if(store.ActiveScheme()!=scheme)throw new IOException("The active power plan changed. Original backup retained.");
                    uint nowAC=store.Read(scheme,Group,Setting),nowDC=store.ReadDC(scheme,Group,Setting);
                    if((nowAC!=0&&nowAC!=original.AC)||(nowDC!=0&&nowDC!=original.DC))throw new IOException("A wireless power value changed outside Elite. Original backup retained.");
                }
            }
        }
    }
}
