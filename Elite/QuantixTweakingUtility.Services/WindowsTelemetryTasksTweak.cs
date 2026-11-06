using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using QuantixTweakingUtility.Models;

namespace QuantixTweakingUtility.Services;

public interface ITelemetryTaskStore
{
    Task<FirewallTask?> Read(string path);
    Task SetEnabled(string path,string fingerprint,bool enabled);
}
public sealed class WindowsTelemetryTaskStore:ITelemetryTaskStore
{
    private readonly WindowsFirewallTaskStore reader=new();
    public async Task<FirewallTask?> Read(string path){
        if(!WindowsTelemetryTasksTweak.Paths.Contains(path,StringComparer.OrdinalIgnoreCase))throw new IOException("Task is outside the telemetry allowlist.");
        try{return await reader.Read(path);}catch(COMException ex) when(ex.HResult==unchecked((int)0x80070002)||ex.HResult==unchecked((int)0x80070003)){return null;}
        catch(FileNotFoundException){return null;}
        catch(DirectoryNotFoundException){return null;}
        catch(COMException ex){throw new IOException("Windows telemetry task inspection failed: "+ex.Message,ex);}
    }
    public Task SetEnabled(string path,string fingerprint,bool enabled)=>WindowsFirewallTaskStore.SetTelemetryEnabled(path,fingerprint,enabled);
}
public static class WindowsTelemetryTasksTweak
{
    public const string Id="gpu.windows-telemetry-tasks.disable";
    private const string Prefix=Id+".task.";
    public static IReadOnlyList<string> Paths {get;}=Array.AsReadOnly(new[]{
        @"\Microsoft\Windows\Application Experience\Microsoft Compatibility Appraiser",
        @"\Microsoft\Windows\Application Experience\ProgramDataUpdater",
        @"\Microsoft\Windows\Customer Experience Improvement Program\Consolidator",
        @"\Microsoft\Windows\Customer Experience Improvement Program\UsbCeip",
        @"\Microsoft\Windows\DiskDiagnostic\Microsoft-Windows-DiskDiagnosticDataCollector"
    });
    private static string Key(string path)=>Prefix+Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(path.ToUpperInvariant())));
    private static void Validate(FirewallTask task){
        if(task==null||!Paths.Contains(task.Path,StringComparer.OrdinalIgnoreCase)||!task.Microsoft||task.Fingerprint==null||!Regex.IsMatch(task.Fingerprint,@"\A[A-F0-9]{64}\z"))
            throw new IOException("Invalid saved telemetry task identity.");
    }
    public static TweakDefinition Build(IRestoreService restore,ITelemetryTaskStore? store=null){
        store??=new WindowsTelemetryTaskStore();var gate=new SemaphoreSlim(1,1);
        return new TweakDefinition{
            Id=Id,Name="Windows Telemetry Tasks Off",Category=TweakCategory.Gpu,RiskLevel=TweakRiskLevel.Medium,RequiresAdministrator=true,
            Description="Disables present Microsoft Compatibility Appraiser, ProgramDataUpdater, CEIP Consolidator/UsbCeip and DiskDiagnostic DataCollector scheduled tasks. May reduce scheduled compatibility/diagnostic work, but can affect upgrade-readiness and diagnostic information. Does not disable every Windows telemetry route, delete tasks or stop current running instances. Elite records each changed task's Enabled state and definition fingerprint; restores only when that same definition still exists. Windows updates may recreate or change tasks.",
            AffectedSettings=Paths.Select(path=>"TaskScheduler:"+path+":Enabled").ToArray(),HasSavedOriginal=()=>restore.GetRestorableTweakIds().Any(key=>key.StartsWith(Prefix,StringComparison.Ordinal)),
            CurrentStateReader=async()=>{try{var tasks=await Present();var saved=Saved();if(tasks.Count==0&&saved.Count==0)return TweakState.Unsupported;return tasks.All(t=>!t.Enabled)&&saved.All(s=>tasks.Any(t=>t.Path.Equals(s.Task.Path,StringComparison.OrdinalIgnoreCase)&&t.Fingerprint==s.Task.Fingerprint&&!t.Enabled))?TweakState.Applied:TweakState.NotApplied;}catch(Exception ex) when(ex is IOException or COMException){return TweakState.Unsupported;}},
            ApplyAction=()=>Change(true),RestoreAction=()=>Change(false)
        };
        List<(string Key,FirewallTask Task)> Saved(){
            var result=new List<(string,FirewallTask)>();
            foreach(string key in restore.GetRestorableTweakIds().Where(k=>k.StartsWith(Prefix,StringComparison.Ordinal))){
                FirewallTask? task;try{task=JsonSerializer.Deserialize<FirewallTask>(restore.GetBackup(key)??throw new IOException("Missing task original."));}catch(JsonException ex){throw new IOException("Unreadable task original.",ex);}
                Validate(task!);if(key!=Key(task!.Path))throw new IOException("Task recovery key does not match its identity.");result.Add((key,task));
            }return result;
        }
        async Task<List<FirewallTask>> Present(){var tasks=new List<FirewallTask>();foreach(string path in Paths){var task=await store.Read(path);if(task==null)continue;Validate(task);if(!task.Path.Equals(path,StringComparison.OrdinalIgnoreCase))throw new IOException("Task provider returned the wrong identity.");tasks.Add(task);}return tasks;}
        async Task<TweakActionResult> Change(bool apply){await gate.WaitAsync();try{
            var saved=Saved();var present=await Present();
            if(apply&&present.Count==0)return TweakActionResult.Fail("No allowlisted telemetry tasks are present on this Windows installation.");
            if(!apply&&saved.Count==0)return TweakActionResult.Fail("No changed telemetry task originals were saved by Elite.");
            var errors=new List<string>();int changed=0;
            var targets=apply?present:saved.Select(s=>s.Task).ToList();
            foreach(var target in targets){try{
                var now=await store.Read(target.Path)??throw new IOException("Recorded task is missing.");Validate(now);
                if(!now.Path.Equals(target.Path,StringComparison.OrdinalIgnoreCase))throw new IOException("Task provider identity mismatch.");
                string key=Key(target.Path);var original=saved.SingleOrDefault(s=>s.Key==key).Task;
                if(original!=null&&now.Fingerprint!=original.Fingerprint)throw new IOException("Task definition changed; original retained for review.");
                if(apply&&original==null){if(!now.Enabled)continue;original=now;string raw=JsonSerializer.Serialize(original);restore.SaveBackup(key,raw);if(restore.GetBackup(key)!=raw)throw new IOException("Task original could not be saved and verified.");}
                bool desired=apply?false:original!.Enabled;
                if(now.Enabled!=desired)await store.SetEnabled(now.Path,now.Fingerprint,desired);
                var after=await store.Read(now.Path);if(after==null||after.Path!=now.Path||after.Fingerprint!=now.Fingerprint||after.Enabled!=desired)throw new IOException("Task Enabled readback failed; original retained.");
                if(!apply)restore.ClearBackup(key);changed++;
            }catch(Exception ex) when(ex is IOException or COMException){errors.Add(target.Path+": "+ex.Message);}}
            if(errors.Count>0)return TweakActionResult.Fail("Some telemetry tasks could not be "+(apply?"disabled":"restored")+". Recovery records retained. "+string.Join(" ",errors));
            return TweakActionResult.Ok(apply?"Allowlisted telemetry task switches disabled and verified. Running instances were not stopped.":changed+" original task switches restored and verified.");
        }finally{gate.Release();}}
    }
}
