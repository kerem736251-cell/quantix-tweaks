using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using QuantixTweakingUtility.Models;

namespace QuantixTweakingUtility.Services;

public record DiscordAccelerationState(string Path,bool? Value);
public interface IDiscordAccelerationStore
{
    bool DiscordRunning { get; }
    DiscordAccelerationState Read();
    void Write(DiscordAccelerationState expected,bool? value);
}
public sealed class WindowsDiscordAccelerationStore:IDiscordAccelerationStore
{
    public const string Setting="enableHardwareAcceleration";
    private readonly string path;
    private readonly Func<bool> running;
    public WindowsDiscordAccelerationStore(string? path=null,Func<bool>? running=null){this.path=System.IO.Path.GetFullPath(path??System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"discord","settings.json"));this.running=running??IsRunning;}
    public bool DiscordRunning=>running();
    private static bool IsRunning(){int session=Process.GetCurrentProcess().SessionId;foreach(var process in Process.GetProcessesByName("Discord")){using(process){try{if(process.SessionId==session)return true;}catch(InvalidOperationException){}}}return false;}
    private byte[] Bytes(){
        for(string? item=path;item!=null;item=System.IO.Path.GetDirectoryName(item)){
            if((File.GetAttributes(item)&FileAttributes.ReparsePoint)!=0)throw new IOException("Linked Discord settings locations are not modified automatically.");
            if(item==System.IO.Path.GetPathRoot(item))break;
        }
        var info=new FileInfo(path);if(info.Length>2*1024*1024)throw new IOException("Discord settings file is unexpectedly large.");return File.ReadAllBytes(path);
    }
    private static (JsonObject Root,bool? Value) Parse(byte[] bytes){
        try{
            // Discord settings are JSON. Preserve unknown properties, but refuse duplicate/ambiguous keys.
            ReadOnlySpan<byte> json=bytes.AsSpan();if(json.StartsWith(new byte[]{0xEF,0xBB,0xBF}))json=json[3..];
            using var document=JsonDocument.Parse(json.ToArray());Validate(document.RootElement);
            if(document.RootElement.ValueKind!=JsonValueKind.Object)throw new IOException("Discord settings must be a JSON object.");
            bool? value=null;if(document.RootElement.TryGetProperty(Setting,out var flag)){
                if(flag.ValueKind is not (JsonValueKind.True or JsonValueKind.False))throw new IOException("Discord hardware acceleration has an unexpected value type.");value=flag.GetBoolean();
            }
            return ((JsonObject)JsonNode.Parse(json)!,value);
        }catch(JsonException ex){throw new IOException("Discord settings could not be parsed; the file was retained.",ex);}
        static void Validate(JsonElement element){
            if(element.ValueKind==JsonValueKind.Object){var properties=element.EnumerateObject().ToArray();if(properties.Select(p=>p.Name).Distinct(StringComparer.Ordinal).Count()!=properties.Length)throw new IOException("Duplicate Discord settings keys are not modified.");foreach(var property in properties)Validate(property.Value);}
            else if(element.ValueKind==JsonValueKind.Array)foreach(var child in element.EnumerateArray())Validate(child);
        }
    }
    public DiscordAccelerationState Read()=>new(path,Parse(Bytes()).Value);
    public void Write(DiscordAccelerationState expected,bool? value){
        if(DiscordRunning)throw new IOException("Fully quit Discord, including its tray icon, before changing hardware acceleration.");
        if(!path.Equals(expected.Path,StringComparison.OrdinalIgnoreCase))throw new IOException("Discord settings path differs from the saved original.");
        byte[] before=Bytes();var (root,current)=Parse(before);if(current!=expected.Value)throw new IOException("Discord hardware acceleration changed outside Elite.");
        if(value.HasValue)root[Setting]=value.Value;else root.Remove(Setting);
        string temporary=path+".quantix-"+Guid.NewGuid().ToString("N")+".tmp";
        try{
            using(var file=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None)){byte[] output=Encoding.UTF8.GetBytes(root.ToJsonString(new JsonSerializerOptions{WriteIndented=true}));file.Write(output);file.Flush(true);}
            if(DiscordRunning||!Bytes().SequenceEqual(before))throw new IOException("Discord or its settings changed before writing. Original file retained.");
            File.Replace(temporary,path,path+".quantix-previous");
            if(Read().Value!=value)throw new IOException("Discord setting readback failed; the saved original was retained.");
        }finally{if(File.Exists(temporary))File.Delete(temporary);}
    }
}
public static class DiscordAccelerationTweak
{
    public const string Id="game.discord-hardware-acceleration.disable";
    public static TweakDefinition Build(IRestoreService restore,IDiscordAccelerationStore? store=null){
        store??=new WindowsDiscordAccelerationStore();var gate=new object();
        return new TweakDefinition{
            Id=Id,Name="Discord Hardware Acceleration Off",Category=TweakCategory.Gaming,RiskLevel=TweakRiskLevel.High,
            Description="Turns off Discord stable's desktop hardware acceleration through its enableHardwareAcceleration setting. Fully quit Discord, including the tray process, before applying or restoring; Elite does not close Discord for you. Restart Discord afterward. This can reduce GPU use but increase CPU load or make Discord rendering slower; it does not guarantee higher game FPS or disable separate video-codec acceleration settings. Saves the exact original Boolean or absence and preserves other JSON settings. PTB/Canary clients are separate.",
            AffectedSettings=new[]{"Discord:settings.json:enableHardwareAcceleration"},HasSavedOriginal=()=>restore.GetBackup(Id)!=null,
            CurrentStateReader=()=>{try{return Task.FromResult(store.Read().Value==false?TweakState.Applied:TweakState.NotApplied);}catch(IOException){return Task.FromResult(TweakState.Unsupported);}},
            ApplyAction=()=>Change(true),RestoreAction=()=>Change(false)
        };
        Task<TweakActionResult> Change(bool apply){lock(gate){
            if(store.DiscordRunning)return Task.FromResult(TweakActionResult.Fail("Fully quit Discord, including the tray icon, before applying or restoring this setting."));
            var current=store.Read();string? raw=restore.GetBackup(Id);DiscordAccelerationState? original;
            try{original=raw==null?null:JsonSerializer.Deserialize<DiscordAccelerationState>(raw);}catch(JsonException ex){throw new IOException("Unreadable Discord original; no setting changed.",ex);}
            if(raw!=null&&(original==null||string.IsNullOrWhiteSpace(original.Path)))throw new IOException("Invalid saved Discord original.");
            if(original!=null&&!current.Path.Equals(original.Path,StringComparison.OrdinalIgnoreCase))throw new IOException("Discord settings location changed. Original retained.");
            if(!apply&&original==null)return Task.FromResult(TweakActionResult.Fail("No original Discord acceleration setting was saved by Elite."));
            if(original!=null&&current.Value!=false&&current.Value!=original.Value)throw new IOException("Discord acceleration changed outside Elite. Original retained.");
            if(original==null){original=current;raw=JsonSerializer.Serialize(original);restore.SaveBackup(Id,raw);if(restore.GetBackup(Id)!=raw)throw new IOException("Discord original could not be saved and verified; no setting changed.");}
            bool? target=apply?false:original.Value;if(current.Value!=target)store.Write(current,target);
            var after=store.Read();if(after.Path!=current.Path||after.Value!=target)throw new IOException("Discord setting readback failed. Original retained.");
            if(!apply)restore.ClearBackup(Id);
            return Task.FromResult(TweakActionResult.Ok(apply?"Discord desktop hardware acceleration disabled and verified in settings. Start Discord to use the change.":"Original Discord hardware acceleration setting restored and verified. Start Discord to use it."));
        }}
    }
}
