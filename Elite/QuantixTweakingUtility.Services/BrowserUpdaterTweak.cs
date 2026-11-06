using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using QuantixTweakingUtility.Models;

namespace QuantixTweakingUtility.Services;

public record BrowserUpdaterState(string Name,string BinaryPath,int StartType,bool Running,int? DelayedAutoStart);
public interface IBrowserUpdaterStore
{
    Task<BrowserUpdaterState[]> Read();
    Task Write(BrowserUpdaterState expected,BrowserUpdaterState target);
}
public sealed class WindowsBrowserUpdaterStore : IBrowserUpdaterStore
{
    public async Task<BrowserUpdaterState[]> Read()
    {
        string script="$rows=@(Get-CimInstance Win32_Service -ErrorAction Stop|Where-Object {$_.Name -match '^(edgeupdate|edgeupdatem|gupdate|gupdatem|GoogleUpdater(Internal)?Service[0-9]+([.][0-9]+){3})$'}|ForEach-Object {$s=$_;if($s.State -notin @('Running','Stopped')){throw 'An updater service is transitioning; retry shortly.'};$key=[Microsoft.Win32.Registry]::LocalMachine.OpenSubKey('SYSTEM\\CurrentControlSet\\Services\\'+$s.Name);if(!$key){throw 'Updater service disappeared.'};try{$type=[int]$key.GetValue('Type');if(($type -band 0x30) -eq 0 -or ($type -band 0xF) -ne 0){throw 'Updater is not a user-mode service.'};if($key.GetValueKind('Start') -ne [Microsoft.Win32.RegistryValueKind]::DWord){throw 'Unexpected service startup type.'};$delay=$null;if('DelayedAutoStart' -in $key.GetValueNames()){if($key.GetValueKind('DelayedAutoStart') -ne [Microsoft.Win32.RegistryValueKind]::DWord){throw 'Unexpected delayed startup type.'};$delay=[int]$key.GetValue('DelayedAutoStart')};[pscustomobject]@{Name=[string]$s.Name;BinaryPath=[string]$key.GetValue('ImagePath',$null,[Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames);StartType=[int]$key.GetValue('Start');Running=($s.State -eq 'Running');DelayedAutoStart=$delay}}finally{$key.Dispose()}});ConvertTo-Json -InputObject $rows -Depth 4 -Compress";
        return JsonSerializer.Deserialize<BrowserUpdaterState[]>(await WindowsAdapterRssStore.Run(script)) ?? throw new IOException("Updater service inspection was unavailable.");
    }
    public async Task Write(BrowserUpdaterState expected,BrowserUpdaterState target)
    {
        BrowserUpdaterTweak.Validate(expected);BrowserUpdaterTweak.Validate(target);
        if(expected.Name!=target.Name||expected.BinaryPath!=target.BinaryPath)throw new IOException("Updater identity cannot change during configuration.");
        var current=(await Read()).SingleOrDefault(state=>state.Name.Equals(expected.Name,StringComparison.OrdinalIgnoreCase));
        if(current!=expected)throw new IOException("Updater service changed before writing. Original backup retained.");
        bool startWhileDisabled=target.Running&&!current.Running&&target.StartType==4;
        await Sc("config",expected.Name,"start=",startWhileDisabled?"demand":target.StartType switch{2=>"auto",3=>"demand",4=>"disabled",_=>throw new IOException("Invalid startup mode.")});
        if(current.Running!=target.Running)await Sc(target.Running?"start":"stop",expected.Name);
        if(startWhileDisabled)await Sc("config",expected.Name,"start=","disabled");
        using(var key=Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\"+expected.Name,true) ?? throw new IOException("Updater service disappeared.")){
            if(target.DelayedAutoStart.HasValue)key.SetValue("DelayedAutoStart",target.DelayedAutoStart.Value,RegistryValueKind.DWord);
            else key.DeleteValue("DelayedAutoStart",false);
        }
        for(int attempt=0;attempt<8;attempt++){
            try{var after=(await Read()).SingleOrDefault(state=>state.Name.Equals(expected.Name,StringComparison.OrdinalIgnoreCase));if(after==target)return;}catch(IOException){}
            if(attempt<7)await Task.Delay(500);
        }
        throw new IOException("Updater service readback did not match. Original backup retained.");
    }
    private static async Task Sc(params string[] arguments)
    {
        var start=new ProcessStartInfo(Path.Combine(Environment.SystemDirectory,"sc.exe")){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
        foreach(string argument in arguments)start.ArgumentList.Add(argument);
        using var process=Process.Start(start) ?? throw new IOException("Windows Service Control could not start.");
        var output=process.StandardOutput.ReadToEndAsync();var error=process.StandardError.ReadToEndAsync();await process.WaitForExitAsync();
        if(process.ExitCode!=0)throw new IOException("Service Control returned "+process.ExitCode+". "+await output+" "+await error);
        await Task.WhenAll(output,error);
    }
}
public static class BrowserUpdaterTweak
{
    public const string Id="debloat.browser-updaters.disable";
    public static bool SupportedName(string name)=>Regex.IsMatch(name,@"\A(?:edgeupdate|edgeupdatem|gupdate|gupdatem|GoogleUpdater(?:Internal)?Service[0-9]+(?:\.[0-9]+){3})\z",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant);
    public static void Validate(BrowserUpdaterState state){
        if(state==null||!SupportedName(state.Name)||string.IsNullOrWhiteSpace(state.BinaryPath)||state.StartType is <2 or >4||state.DelayedAutoStart is not null and not 0 and not 1)
            throw new IOException("Invalid updater service identity or configuration.");
    }
    public static TweakDefinition Build(IRestoreService restore,IBrowserUpdaterStore? store=null)
    {
        store??=new WindowsBrowserUpdaterStore();var gate=new SemaphoreSlim(1,1);
        return new TweakDefinition{
            Id=Id,Name="Browser Updater Services Off",Category=TweakCategory.Windows,RiskLevel=TweakRiskLevel.High,RequiresAdministrator=true,
            Description="Stops and disables installed machine-level Edge and Google updater services, including versioned GoogleUpdater service names. This blocks service-based security updates for browsers and may also affect WebView2 or other Google applications. Scheduled tasks, per-user updaters and reinstall/repair can remain active; checking browser Help does not guarantee updates still work while these services are disabled. Restore before updating. Elite saves each exact service's binary path, startup mode, delayed-start state and running state. Changed or missing services retain their recovery record for review.",
            AffectedSettings=new[]{"services:browser-updaters:start-running-delayed"},HasSavedOriginal=()=>restore.GetBackup(Id)!=null,
            CurrentStateReader=async()=>{try{var states=await Inspect();return states.Length==0?TweakState.Unsupported:states.All(s=>s.StartType==4&&!s.Running)?TweakState.Applied:TweakState.NotApplied;}catch(IOException){return TweakState.Unsupported;}},
            ApplyAction=()=>Change(true),RestoreAction=()=>Change(false)
        };
        async Task<BrowserUpdaterState[]> Inspect(){var states=await store.Read();foreach(var state in states)Validate(state);if(states.Select(s=>s.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=states.Length)throw new IOException("Duplicate updater identities.");return states;}
        async Task<TweakActionResult> Change(bool apply){
            await gate.WaitAsync();try{
                var current=await Inspect();string? raw=restore.GetBackup(Id);BrowserUpdaterState[]? original;
                try{original=raw==null?null:JsonSerializer.Deserialize<BrowserUpdaterState[]>(raw);}catch(JsonException ex){throw new IOException("Unreadable updater originals; no services changed.",ex);}
                if(raw!=null&&(original==null||original.Length==0))throw new IOException("Invalid updater originals; no services changed.");
                if(original!=null){foreach(var state in original)Validate(state);if(original.Select(s=>s.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=original.Length)throw new IOException("Duplicate saved updater identities.");}
                if(!apply&&original==null)return TweakActionResult.Fail("No original updater service settings were saved by Elite.");
                if(apply&&current.Length==0)return TweakActionResult.Fail("No supported machine updater services are installed.");
                if(apply&&original!=null&&!current.Select(s=>s.Name).OrderBy(n=>n,StringComparer.OrdinalIgnoreCase).SequenceEqual(original.Select(s=>s.Name).OrderBy(n=>n,StringComparer.OrdinalIgnoreCase),StringComparer.OrdinalIgnoreCase))
                    throw new IOException("The updater service set changed. Restore the recorded services before applying again.");
                if(original==null){original=current;raw=JsonSerializer.Serialize(original);restore.SaveBackup(Id,raw);if(restore.GetBackup(Id)!=raw)throw new IOException("Updater backup verification failed; no services changed.");}
                var errors=new List<string>();
                foreach(var saved in original){
                    try{
                        var now=(await Inspect()).SingleOrDefault(s=>s.Name.Equals(saved.Name,StringComparison.OrdinalIgnoreCase)) ?? throw new IOException("Recorded updater service is missing.");
                        var disabled=saved with{StartType=4,Running=false};
                        if(now.BinaryPath!=saved.BinaryPath || (now.StartType!=saved.StartType&&now.StartType!=4&&!(saved.StartType==4&&saved.Running&&now.StartType==3))
                            || (now.Running&&!saved.Running) || (now.DelayedAutoStart!=saved.DelayedAutoStart&&now.DelayedAutoStart!=0))
                            throw new IOException("Service identity or settings changed outside Elite.");
                        var desired=apply?disabled:saved;if(now!=desired)await store.Write(now,desired);
                        if(!(await Inspect()).Contains(desired))throw new IOException("Updater readback failed.");
                    }catch(IOException ex){errors.Add(saved.Name+": "+ex.Message);}
                }
                if(errors.Count>0)return TweakActionResult.Fail("Some updater services could not be "+(apply?"disabled":"restored")+". Originals retained. "+string.Join(" ",errors));
                if(!apply)restore.ClearBackup(Id);
                return TweakActionResult.Ok(apply?"Recorded browser updater services disabled and verified. Restore before installing security updates.":"Original updater service startup, delay and running states restored and verified.");
            }finally{gate.Release();}
        }
    }
}
