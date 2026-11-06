using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Win32;
using QuantixTweakingUtility.Models;

namespace QuantixTweakingUtility.Services;

public record RegistryStringSetting(RegistryKey Hive,string AffectedSetting,string Path,string Name,string Target);
public static class RegistryStringTweaks
{
    public static TweakDefinition Build(string id,string name,string description,TweakCategory category,TweakRiskLevel risk,
        IReadOnlyList<RegistryStringSetting> settings,IRestoreService restore,bool administrator=false,bool restart=false)
    {
        var gate=new object();
        return new TweakDefinition
        {
            Id=id,Name=name,Description=description,Category=category,RiskLevel=risk,RequiresAdministrator=administrator,RequiresRestart=restart,
            AffectedSettings=settings.Select(item=>item.AffectedSetting).ToArray(),
            CurrentStateReader=()=>
            {
                lock(gate)
                {
                    try{return Task.FromResult(settings.All(item=>Read(item)==item.Target)?TweakState.Applied:TweakState.NotApplied);}
                    catch(IOException){return Task.FromResult(TweakState.Unsupported);}
                }
            },
            ApplyAction=()=>Change(true),RestoreAction=()=>Change(false)
        };
        Task<TweakActionResult> Change(bool apply)
        {
            lock(gate)
            {
                var current=settings.Select(Read).ToArray();string? saved=restore.GetBackup(id);string?[]? original;
                try{original=saved==null?null:JsonSerializer.Deserialize<string?[]>(saved);}
                catch(JsonException){return Task.FromResult(TweakActionResult.Fail("The recorded string values are invalid. No settings changed."));}
                if(saved!=null&&(original==null||original.Length!=settings.Count))return Task.FromResult(TweakActionResult.Fail("The recorded string values are invalid. No settings changed."));
                if(!apply&&original==null)return Task.FromResult(TweakActionResult.Fail("No previous string values were recorded."));
                if(original!=null&&current.Where((value,index)=>value!=settings[index].Target&&value!=original[index]).Any())
                    return Task.FromResult(TweakActionResult.Fail("A string setting changed outside Elite. Original backup retained; no settings changed."));
                if(apply&&original==null)
                {
                    restore.SaveBackup(id,JsonSerializer.Serialize(current));
                    original=JsonSerializer.Deserialize<string?[]>(restore.GetBackup(id)??"null");
                    if(original==null||!original.SequenceEqual(current))throw new IOException("String backup verification failed. No settings changed.");
                }
                for(int index=0;index<settings.Count;index++)
                {
                    var item=settings[index];
                    if(Read(item)!=current[index])throw new IOException("A string setting changed before writing. Original backup retained.");
                    string? target=apply?item.Target:original![index];
                    if(target==null)
                    {
                        using var key=item.Hive.OpenSubKey(item.Path,true);key?.DeleteValue(item.Name,false);
                    }
                    else
                    {
                        using var key=item.Hive.CreateSubKey(item.Path,true);key.SetValue(item.Name,target,RegistryValueKind.String);
                    }
                    if(Read(item)!=target)throw new IOException("String value readback failed. Original backup retained.");
                }
                if(!apply)restore.ClearBackup(id);
                return Task.FromResult(TweakActionResult.Ok(name+(apply?" applied and verified.":" restored and verified.")));
            }
        }
    }
    private static string? Read(RegistryStringSetting item)
    {
        using var key=item.Hive.OpenSubKey(item.Path);
        if(key==null||!key.GetValueNames().Contains(item.Name,StringComparer.OrdinalIgnoreCase))return null;
        if(key.GetValueKind(item.Name)!=RegistryValueKind.String)throw new IOException("The string setting has an unsupported registry type; no original value was discarded.");
        return key.GetValue(item.Name,null,RegistryValueOptions.DoNotExpandEnvironmentNames) as string ?? throw new IOException("The string setting could not be read.");
    }
}
