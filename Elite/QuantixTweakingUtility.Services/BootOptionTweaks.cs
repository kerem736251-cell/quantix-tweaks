using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Intrinsics.X86;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using QuantixTweakingUtility.Models;

namespace QuantixTweakingUtility.Services;

public record BootOptionSnapshot(Guid Entry,Dictionary<string,string?> Values);
public interface IBootOptionStore
{
    Task<BootOptionSnapshot> Read(string[] options);
    Task Write(Guid entry,string option,string? value);
}
public sealed class WindowsBootOptionStore:IBootOptionStore
{
    public async Task<BootOptionSnapshot> Read(string[] options)=>Parse(await Run("/enum","{current}","/v"),options);
    public static BootOptionSnapshot Parse(string output,string[] options)
    {
        var identity=Regex.Match(output,@"(?m)^[^{}\r\n]+(\{[0-9a-fA-F-]{36}\})\s*$");
        if(!identity.Success||!Guid.TryParse(identity.Groups[1].Value,out var entry)||entry==Guid.Empty)throw new IOException("The current boot entry's full identity could not be verified.");
        var values=new Dictionary<string,string?>();
        foreach(string option in options){
            _=Normalize(option,null);
            var lines=output.Split('\n').Where(line=>Regex.IsMatch(line,@"^\s*"+Regex.Escape(option)+@"(?:\s|$)",RegexOptions.IgnoreCase)).ToArray();
            if(lines.Length>1)throw new IOException("Duplicate boot option output.");
            if(lines.Length==0){values.Add(option,null);continue;}
            var match=Regex.Match(lines[0],@"^\s*"+Regex.Escape(option)+@"\s+(\S+)\s*$",RegexOptions.IgnoreCase);
            if(!match.Success)throw new IOException("Unrecognized boot option output.");
            values.Add(option,Normalize(option,match.Groups[1].Value));
        }
        return new(entry,values);
    }
    public static string? Normalize(string option,string? value)
    {
        string[] allowed=option switch{"useplatformclock" or "disabledynamictick"=>new[]{"yes","no"},"tscsyncpolicy"=>new[]{"default","legacy","enhanced"},"x2apicpolicy"=>new[]{"default","enable","disable"},_=>throw new IOException("Unknown boot option.")};
        if(value==null)return null;value=value.ToLowerInvariant();
        if(option is "useplatformclock" or "disabledynamictick")value=value switch{"true" or "ja"=>"yes","false" or "nein"=>"no",_=>value};
        if(option=="x2apicpolicy")value=value switch{"enabled"=>"enable","disabled"=>"disable",_=>value};
        return allowed.Contains(value,StringComparer.Ordinal)?value:throw new IOException("The boot option has an unrecognized value; no setting was guessed.");
    }
    public Task Write(Guid entry,string option,string? value)
    {
        if(entry==Guid.Empty)throw new IOException("A full boot-entry identity is required.");
        value=Normalize(option,value);
        return value==null?Run("/deletevalue",entry.ToString("B"),option):Run("/set",entry.ToString("B"),option,value);
    }
    private static async Task<string> Run(params string[] arguments)
    {
        var start=new ProcessStartInfo(Path.Combine(Environment.SystemDirectory,"bcdedit.exe")){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
        foreach(string argument in arguments)start.ArgumentList.Add(argument);
        using var process=Process.Start(start)??throw new IOException("Windows Boot Configuration Editor could not start.");
        var output=process.StandardOutput.ReadToEndAsync();var error=process.StandardError.ReadToEndAsync();await process.WaitForExitAsync();
        string text=await output,errors=await error;if(process.ExitCode!=0)throw new IOException("Windows boot configuration was unavailable or rejected the request ("+process.ExitCode+"). "+text+" "+errors);
        return text;
    }
}
public static class BootOptionTweaks
{
    private enum Kind { Platform, X2Apic, DynamicTick }
    private sealed record Original(Guid Entry,Dictionary<string,string?> Values);
    public static bool SupportsX2Apic()=>X86Base.IsSupported&&(X86Base.CpuId(1,0).Ecx&(1<<21))!=0;
    public static TweakDefinition Build(bool x2apic,IRestoreService restore,IBootOptionStore? store=null,Func<bool>? supportsX2Apic=null)
        => Create(x2apic?Kind.X2Apic:Kind.Platform,restore,store,supportsX2Apic);
    public static TweakDefinition Dynamic(IRestoreService restore,IBootOptionStore? store=null)=>Create(Kind.DynamicTick,restore,store,null);
    private static TweakDefinition Create(Kind kind,IRestoreService restore,IBootOptionStore? store,Func<bool>? supportsX2Apic)
    {
        bool x2apic=kind==Kind.X2Apic,dynamicTick=kind==Kind.DynamicTick;
        store??=new WindowsBootOptionStore();supportsX2Apic??=SupportsX2Apic;var gate=new SemaphoreSlim(1,1);
        string id=dynamicTick?"power.dynamic-tick.disable":x2apic?"boot.x2apic.enable":"boot.platform-clock-tsc";
        var targets=dynamicTick?new Dictionary<string,string>{{"disabledynamictick","yes"}}:x2apic?new Dictionary<string,string>{{"x2apicpolicy","enable"}}:new Dictionary<string,string>{{"useplatformclock","no"},{"tscsyncpolicy","enhanced"}};
        string[] keys=targets.Keys.ToArray();
        return new TweakDefinition{
            Id=id,Name=dynamicTick?"Disable Dynamic Tick":x2apic?"x2apic Enable":"On",Category=TweakCategory.Windows,RiskLevel=TweakRiskLevel.High,RequiresAdministrator=true,RequiresRestart=true,
            Description=dynamicTick?"Sets disabledynamictick to Yes for this Windows boot entry. Disables idle tick suppression and may increase wakeups and power use. This is a boot/debugging control, not a guaranteed frame-pacing improvement; boot changes can cause compatibility or startup problems. Saves the exact original option or absence for this entry. Restart after applying or restoring.":x2apic?"Requests x2APIC interrupt-controller policy Enable for this Windows boot entry, only if CPUID reports x2APIC capability. Firmware, Windows and hardware still determine actual mode; forcing a boot policy can cause compatibility or startup problems. No latency improvement is guaranteed. Saves the original option or absence for this exact boot entry. Restart after applying or restoring.":"Sets useplatformclock to No and TSC synchronization policy to Enhanced for this Windows boot entry. This stops forcing a platform-backed performance counter; it does not disable HPET hardware or guarantee a particular active timer or better performance. Microsoft documents these timer options for debugging. Boot changes can cause timing or startup problems. Elite preserves both originals or absence for this exact entry. Restart after applying or restoring.",
            AffectedSettings=keys.Select(k=>"bcd:current:"+k).ToArray(),HasSavedOriginal=()=>restore.GetBackup(id)!=null,
            CurrentStateReader=async()=>{try{if(x2apic&&!supportsX2Apic()&&restore.GetBackup(id)==null)return TweakState.Unsupported;var current=await Inspect();return targets.All(p=>current.Values[p.Key]==p.Value)?TweakState.Applied:TweakState.NotApplied;}catch(IOException){return TweakState.Unsupported;}},
            ApplyAction=()=>Change(true),RestoreAction=()=>Change(false)
        };
        async Task<BootOptionSnapshot> Inspect(){var current=await store.Read(keys);if(current.Entry==Guid.Empty||current.Values==null||keys.Any(k=>!current.Values.ContainsKey(k)))throw new IOException("Boot entry inspection was incomplete.");foreach(string key in keys)if(WindowsBootOptionStore.Normalize(key,current.Values[key])!=current.Values[key])throw new IOException("Noncanonical boot option state.");return current;}
        async Task<TweakActionResult> Change(bool apply){await gate.WaitAsync();try{
            if(apply&&x2apic&&!supportsX2Apic())throw new IOException("This processor does not report x2APIC capability.");
            var current=await Inspect();string? raw=restore.GetBackup(id);Original? original;
            if(dynamicTick&&raw!=null&&!raw.StartsWith("{",StringComparison.Ordinal))return TweakActionResult.Fail("This legacy dynamic-tick backup has no boot-entry identity. No option changed; the original record is retained for manual recovery rather than assuming it belongs to the current entry.");
            try{original=raw==null?null:JsonSerializer.Deserialize<Original>(raw);}catch(JsonException ex){throw new IOException("Unreadable boot originals; no options changed.",ex);}
            if(raw!=null&&(original==null||original.Entry==Guid.Empty||original.Values==null||original.Values.Count!=keys.Length||keys.Any(k=>!original.Values.ContainsKey(k))))throw new IOException("Invalid saved boot originals.");
            if(original!=null){if(original.Entry!=current.Entry)throw new IOException("The current boot entry differs from the saved original. No options changed.");foreach(string key in keys)if(WindowsBootOptionStore.Normalize(key,original.Values[key])!=original.Values[key])throw new IOException("Invalid saved boot value.");}
            if(!apply&&original==null)return TweakActionResult.Fail("No original boot options were saved by Elite.");
            if(original==null){original=new(current.Entry,keys.ToDictionary(k=>k,k=>current.Values[k]));raw=JsonSerializer.Serialize(original);restore.SaveBackup(id,raw);if(restore.GetBackup(id)!=raw)throw new IOException("Boot backup verification failed; no options changed.");}
            foreach(string key in keys){
                var now=await Inspect();if(now.Entry!=original.Entry)throw new IOException("Boot entry changed during the operation. Originals retained.");
                if(keys.Any(k=>now.Values[k]!=original.Values[k]&&now.Values[k]!=targets[k]))throw new IOException("A boot option changed outside Elite. Originals retained.");
                string? desired=apply?targets[key]:original.Values[key];if(now.Values[key]!=desired)await store.Write(original.Entry,key,desired);
                var after=await Inspect();if(after.Entry!=original.Entry||after.Values[key]!=desired)throw new IOException("Boot option readback failed. Originals retained.");
            }
            var final=await Inspect();
            if(final.Entry!=original.Entry||keys.Any(k=>final.Values[k]!=(apply?targets[k]:original.Values[k])))
                throw new IOException("The boot option group changed during final verification. Originals retained.");
            if(!apply)restore.ClearBackup(id);
            return TweakActionResult.Ok("Boot options "+(apply?"configured":"restored")+" and read back for the recorded entry. Restart Windows; active hardware mode is not measured by this check.");
        }finally{gate.Release();}}
    }
}
