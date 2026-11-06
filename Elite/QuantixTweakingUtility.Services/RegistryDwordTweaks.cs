using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Win32;
using QuantixTweakingUtility.Models;

namespace QuantixTweakingUtility.Services;

public static class RegistryDwordTweaks
{
    public static TweakDefinition Build(string id,string name,string description,TweakCategory category,TweakRiskLevel risk,RegistryKey hive,
        string affectedSetting,string path,string valueName,int target,IRestoreService restore,bool administrator=false,bool restart=false)
    {
        var gate=new object();
        return new TweakDefinition
        {
            Id=id,Name=name,Description=description,Category=category,RiskLevel=risk,RequiresAdministrator=administrator,RequiresRestart=restart,
            AffectedSettings=new[]{affectedSetting},
            HasSavedOriginal=()=>restore.GetBackup(id)!=null,
            CurrentStateReader=()=>
            {
                lock(gate)
                {
                    try{return Task.FromResult(Read()==target?TweakState.Applied:TweakState.NotApplied);}
                    catch(IOException){return Task.FromResult(TweakState.Unsupported);}
                }
            },
            ApplyAction=()=>Change(true),RestoreAction=()=>Change(false)
        };
        int? Read()
        {
            using var key=hive.OpenSubKey(path);
            if(key==null||!key.GetValueNames().Contains(valueName,StringComparer.OrdinalIgnoreCase))return null;
            if(key.GetValueKind(valueName)!=RegistryValueKind.DWord||key.GetValue(valueName) is not int value)
                throw new IOException("The setting has an unsupported registry type. No original value was discarded.");
            return value;
        }
        Task<TweakActionResult> Change(bool apply)
        {
            lock(gate)
            {
                int? current=Read(),original=null;string? saved=restore.GetBackup(id);
                if(saved!=null&&saved!="null")
                {
                    if(!int.TryParse(saved,out int parsed))return Task.FromResult(TweakActionResult.Fail("The recorded DWORD original is invalid. No settings changed."));
                    original=parsed;
                }
                if(!apply&&saved==null)return Task.FromResult(TweakActionResult.Fail("No previous value was recorded."));
                if(saved!=null&&current!=target&&current!=original)
                    return Task.FromResult(TweakActionResult.Fail("The setting changed outside Elite. Original backup retained; no setting changed."));
                if(apply&&saved==null)
                {
                    string serialized=current?.ToString()??"null";restore.SaveBackup(id,serialized);
                    if(restore.GetBackup(id)!=serialized)throw new IOException("DWORD backup verification failed. No setting changed.");
                }
                if(Read()!=current)throw new IOException("The DWORD setting changed before writing. Original backup retained.");
                int? desired=apply?target:original;
                if(desired.HasValue)RegistryHelper.WriteDword(hive,path,valueName,desired.Value);
                else RegistryHelper.DeleteValue(hive,path,valueName);
                if(Read()!=desired)throw new IOException("DWORD readback failed. Original backup retained.");
                if(!apply)restore.ClearBackup(id);
                return Task.FromResult(TweakActionResult.Ok(name+(apply?" applied and verified.":" restored and verified.")));
            }
        }
    }
}
