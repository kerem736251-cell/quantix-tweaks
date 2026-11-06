using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace QuantixTweakingUtility.Services;
public interface IGameGraphicsStore { string? Read(string path);void Write(string path,string? value); }
public sealed class GameGraphicsRegistry : IGameGraphicsStore {
    private const string Key="Software\\Microsoft\\DirectX\\UserGpuPreferences";
    public string? Read(string path) {using var key=Registry.CurrentUser.OpenSubKey(Key);if(key==null || !key.GetValueNames().Contains(path,StringComparer.OrdinalIgnoreCase))return null;if(key.GetValueKind(path)!=RegistryValueKind.String)throw new IOException("Unexpected graphics preference format. No changes made.");return (string?)key.GetValue(path,null,RegistryValueOptions.DoNotExpandEnvironmentNames);}
    public void Write(string path,string? value) {if(value==null){using var key=Registry.CurrentUser.OpenSubKey(Key,true);key?.DeleteValue(path,false);}else {using var key=Registry.CurrentUser.CreateSubKey(Key);key.SetValue(path,value,RegistryValueKind.String);} }
}
public sealed record GraphicsOriginal(string Path,string Feature,string? Original,string Target);
public sealed record GraphicsJournal(int Version,List<GraphicsOriginal> Entries);
public sealed record GraphicsCapabilities(int Build,string[] Adapters) {
    public bool HighPerformance => Build>=17134 && Adapters.Length>1;
    public bool Windowed => Build>=22621;
}
public sealed class GameGraphicsService {
    private readonly IGameGraphicsStore store;
    private readonly string root;
    private readonly Func<GraphicsCapabilities> capability;
    private readonly bool validateFile;
    public GameGraphicsService():this(new GameGraphicsRegistry(),Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Quantix","Elite","Graphics"),Detect,true){}
    public GameGraphicsService(IGameGraphicsStore store,string root,Func<GraphicsCapabilities> capability,bool validateFile=true){this.store=store;this.root=root;this.capability=capability;this.validateFile=validateFile;}
    public static GraphicsCapabilities Detect() {
        using var search=new ManagementObjectSearcher("SELECT Name,PNPDeviceID,ConfigManagerErrorCode FROM Win32_VideoController");using var results=search.Get();var adapters=new List<string>();
        foreach(ManagementObject item in results)using(item){string id=item["PNPDeviceID"]?.ToString()??"";if(id.StartsWith("PCI\\",StringComparison.OrdinalIgnoreCase) && Convert.ToUInt32(item["ConfigManagerErrorCode"]??1)==0)adapters.Add(item["Name"]?.ToString()??"Graphics adapter");}
        return new(Environment.OSVersion.Version.Build,adapters.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
    }
    public static string Target(string feature)=>feature switch {"GpuPreference"=>"2","SwapEffectUpgradeEnable"=>"1",_=>throw new IOException("Unknown graphics feature.")};
    private static Match[] Tokens(string? value) {
        string text=value??"";var matches=Regex.Matches(text,@"(?<key>[A-Za-z][A-Za-z0-9_]*)=(?<value>[^;\r\n]*);").Cast<Match>().ToArray();
        if(string.Concat(matches.Select(m=>m.Value))!=text || matches.Select(m=>m.Groups["key"].Value).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=matches.Length)throw new IOException("Graphics preferences are malformed or ambiguous. No changes made.");
        return matches;
    }
    public static string? Token(string? value,string feature)=>Tokens(value).SingleOrDefault(m=>m.Groups["key"].Value.Equals(feature,StringComparison.OrdinalIgnoreCase))?.Groups["value"].Value;
    public static string? Patch(string? value,string feature,string? target) {
        Target(feature);string text=value??"";var match=Tokens(value).SingleOrDefault(m=>m.Groups["key"].Value.Equals(feature,StringComparison.OrdinalIgnoreCase));
        if(match!=null){string replacement=target==null?"":match.Groups["key"].Value+"="+target+";";text=text.Substring(0,match.Index)+replacement+text.Substring(match.Index+match.Length);}
        else if(target!=null)text+=feature+"="+target+";";
        return text.Length==0?null:text;
    }
    private string JournalPath=>Path.Combine(root,"originals-v1.json");
    private GraphicsJournal Load() {
        if(!File.Exists(JournalPath))return new(1,new());
        var journal=JsonSerializer.Deserialize<GraphicsJournal>(File.ReadAllText(JournalPath))??throw new IOException("Graphics backup is invalid.");
        if(journal.Version!=1 || journal.Entries==null || journal.Entries.Any(e=>string.IsNullOrWhiteSpace(e.Path)||e.Target!=Target(e.Feature)) || journal.Entries.GroupBy(e=>e.Path.ToUpperInvariant()+"|"+e.Feature).Any(g=>g.Count()>1))throw new IOException("Graphics backup is invalid. Recovery data retained.");
        foreach(var entry in journal.Entries)Tokens(entry.Original);
        return journal;
    }
    private void Save(GraphicsJournal journal) {
        string temp=JournalPath+"."+Guid.NewGuid().ToString("N")+".tmp";byte[] data=JsonSerializer.SerializeToUtf8Bytes(journal);
        using(var file=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None)){file.Write(data);file.Flush(true);}File.Move(temp,JournalPath,true);
    }
    private string PathFor(string path) {string full=Path.GetFullPath(path);if(!full.EndsWith(".exe",StringComparison.OrdinalIgnoreCase)||validateFile&&!File.Exists(full))throw new IOException("Select the game's actual EXE file.");return full;}
    public string? Current(string path,string feature)=>Token(store.Read(PathFor(path)),feature);
    public bool HasOriginal(string path,string feature)=>Load().Entries.Any(e=>e.Path.Equals(PathFor(path),StringComparison.OrdinalIgnoreCase)&&e.Feature==feature);
    public void Apply(string path,string feature) {
        string full=PathFor(path);string target=Target(feature);var caps=capability();if(feature=="GpuPreference"?!caps.HighPerformance:!caps.Windowed)throw new IOException(feature=="GpuPreference"?"Requires Windows graphics preferences and multiple active graphics adapters.":"Requires Windows 11 version 22H2 or newer.");
        Directory.CreateDirectory(root);using var gate=new FileStream(Path.Combine(root,"graphics.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
        var journal=Load();string? current=store.Read(full);string? token=Token(current,feature);var entry=journal.Entries.SingleOrDefault(e=>e.Path.Equals(full,StringComparison.OrdinalIgnoreCase)&&e.Feature==feature);
        if(entry!=null && token!=entry.Target && token!=Token(entry.Original,feature))throw new IOException("This setting changed outside Elite. Restore or review it before applying again.");
        if(token==target)return;
        if(entry==null){journal.Entries.Add(new(full,feature,current,target));Save(journal);}
        string? proposed=Patch(current,feature,target);store.Write(full,proposed);if(store.Read(full)!=proposed)throw new IOException("Graphics setting did not read back. Original backup retained.");
    }
    public void Restore(string path,string feature) {
        string full=PathFor(path);Target(feature);Directory.CreateDirectory(root);using var gate=new FileStream(Path.Combine(root,"graphics.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
        var journal=Load();var entry=journal.Entries.SingleOrDefault(e=>e.Path.Equals(full,StringComparison.OrdinalIgnoreCase)&&e.Feature==feature)??throw new IOException("No original value saved for this setting.");
        string? current=store.Read(full);string? original=Token(entry.Original,feature);string? token=Token(current,feature);
        if(token!=entry.Target && token!=original)throw new IOException("This setting changed outside Elite. Restore stopped to preserve the change.");
        string? proposed=Patch(current,feature,original);if(proposed==null && entry.Original=="")proposed="";
        store.Write(full,proposed);if(store.Read(full)!=proposed)throw new IOException("Restore did not read back. Original backup retained.");journal.Entries.Remove(entry);Save(journal);
    }
}
