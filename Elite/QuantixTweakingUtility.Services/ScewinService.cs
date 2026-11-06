using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace QuantixTweakingUtility.Services;

public sealed record BiosOption(string Value,string Label,bool Selected);
public sealed record BiosQuestion(string Key,string Name,string Block,int Offset,int Length,BiosOption[] Options);
public sealed record BiosPreset(string Title,string[] Names,string Target,string Detail,string Vendor="Any",string Category="Power",string Block="",bool Group=false);
public sealed record BiosOriginal(string Key,string Name,string Original,string Target,string? Previous=null);
public sealed record BiosJournal(string Hardware,List<BiosOriginal> Entries);

public sealed class ScewinService
{
    public static readonly BiosPreset[] Presets = {
        new("AMD automatic CPU boost",new[]{"Core Performance Boost"},"Auto","Allows the processor's stock automatic boost algorithm. Can recover performance when boost was disabled; no extra gain is expected when already Auto. Thermal and power headroom still limit boost. Does not change PBO, voltage or power limits.","AMD","Gaming performance",Group:true),
        new("Intel Turbo Boost",new[]{"Intel Turbo Boost Technology","Intel(R) Turbo Boost Technology","Turbo Mode"},"Enabled","Allows supported Intel processors to boost above base frequency within their existing limits. Can recover performance when turbo was disabled; no extra gain is expected when already enabled. Does not change voltage or power limits.","Intel","Gaming performance"),
        new("Intel Speed Shift",new[]{"Intel Speed Shift Technology","Intel(R) Speed Shift Technology"},"Enabled","Lets a supported CPU respond quickly to workload changes through hardware performance control. Benchmark CPU-limited games; an FPS gain is not guaranteed.","Intel","Gaming performance"),
        new("Intel SpeedStep",new[]{"Intel SpeedStep","Intel(R) SpeedStep(tm)","Enhanced Intel SpeedStep Technology"},"Enabled","Preserves dynamic CPU frequency control and turbo prerequisites on supported Intel systems. Does not force maximum clocks or change voltage. No additional gain is expected when already enabled.","Intel","Gaming performance"),
        new("AMD memory-channel interleaving",new[]{"Memory interleaving","Memory Channel Interleaving"},"Enabled","Allows firmware-supported memory-channel interleaving. Can recover bandwidth when interleaving was disabled; Auto may already enable it. Test memory stability and CPU-limited games after reboot. Does not change memory clocks, timings or voltages.","AMD","Gaming performance"),
        new("PCIe ASPM",new[]{"ASPM Support","PCI Express Native ASPM","ASPM Control for CPU PCIe","ASPM Mode(Dev#1/Func#2)","ASPM Mode(Dev#2/Func#1)","ASPM Mode(Dev#2/Func#2)","ASPM Mode Control(Device4)","ASPM Mode Control(Device5)","ASPM Mode Control(Device6)","ASPM Mode Control(Device7)","ASPM"},"Disabled","One switch disables the matched CPU, chipset and PCIe-port ASPM preferences together. Each original is saved separately. May increase idle power; compare device stability, sleep and latency before keeping it.",Category:"PCIe",Group:true),
        new("Energy Efficient Turbo experiment",new[]{"Energy Efficient Turbo"},"Disabled","May increase power or temperature; an FPS improvement is not guaranteed.","Intel"),
        new("Energy Efficient P-state experiment",new[]{"Energy Efficient P-state","Energy Efficient P-State"},"Disabled","Changes firmware power preference; test thermals and performance before keeping it.","Intel"),
        new("Spread spectrum experiment",new[]{"Spread Spectrum","CPU Spread Spectrum"},"Disabled","Disables clock modulation; increases electromagnetic emissions. Leave unchanged unless testing a specific issue.",Category:"Clocks"),
        new("PCIe clock gating",new[]{"PCIe Clock Gating","PCI Express Clock Gating"},"Disabled","Keeps the PCIe controller clock active; may increase idle power.","Intel","PCIe"),
        new("Latency Tolerance Reporting",new[]{"LTR Support","Latency Tolerance Reporting"},"Disabled","Changes device power negotiation; compare latency and device stability.","Intel","PCIe"),
        new("Race to Halt",new[]{"Race To Halt (RTH)","Race To Halt"},"Disabled","Changes the CPU idle strategy; benchmark power, temperature and boost.","Intel"),
        new("Optimized Buffer Flush / Fill",new[]{"OBFF","OBFF Support"},"Disabled","Changes PCIe traffic power coordination; improvements are not guaranteed.","Intel","PCIe"),
        new("PCI delay optimization",new[]{"PCI Delay Optimization"},"Enabled","Changes firmware device initialization. Restore if a device stops initializing.","Intel","PCIe"),
        new("Fabric C-states",new[]{"DF Cstates","DF C-States","DF Cstates Control"},"Disabled","Keeps the AMD fabric out of its idle state; increases idle power. Test memory and device stability.","AMD"),
        new("Fusion Message C State",new[]{"Fusion Message C State","Fusion Message C-state"},"Disabled","Disables this exposed AMD idle-state message preference. May increase idle power; compare boost, temperatures and sleep after reboot.","AMD"),
        new("SB C1E Support",new[]{"SB C1E Support"},"Disabled","Disables this exposed southbridge idle preference. May increase idle power; verify peripheral and sleep behavior.","AMD"),
        new("Disable memory power-down",new[]{"Power Down Enable","DRAM Power Down Enable","Memory Power Down"},"Disabled","Disables the exposed DDR power-down preference. May increase memory power and alter memory-context behavior. Test memory stability after reboot; each exposed copy is updated and backed up separately.",Category:"Memory",Group:true),
        new("Disable ErP Ready",new[]{"ErP Ready","ErP Support"},"Disabled","Allows higher standby power and may permit powered USB or wake features while the PC is off. This is a standby-behavior option, not an FPS boost.",Category:"Standby"),
        new("Disable ACPI standby",new[]{"ACPI Standby State"},"Suspend Disabled","Disables the firmware standby preference: Windows sleep may no longer work. Does not improve active gaming performance; use only when intentionally disabling standby.",Category:"Standby"),
        new("AB clock gating",new[]{"AB Clock Gating"},"Disabled","Keeps the Advanced Bus fabric clock active; may increase power and heat.","AMD"),
        new("Chipset power saving",new[]{"Chipset Power Saving Features","Chipset Power Saving"},"Disabled","Changes chipset idle behavior. Test sleep, USB and PCIe devices after reboot.","AMD"),
        new("AMD Cool'n'Quiet",new[]{"AMD Cool'n'Quiet","Cool'n'Quiet"},"Disabled","May raise idle power without improving games. Compare CPU boost and temperature.","AMD"),
        new("CPU core C-states",new[]{"Global C-state Control","CPU C States","CPU C-States"},"Disabled","Disabling core idle can reduce single-core boost, including on X3D processors.",Category:"CPU",Group:true),
        new("ECO mode",new[]{"ECO Mode","Eco Mode"},"Disabled","Removing the lower-power limit can increase temperature and power demand.","AMD","CPU", "Cooling and board power limits need validation. Set this in the motherboard BIOS."),
        new("SoC / Uncore full speed",new[]{"SoC/Uncore OC Mode","SoC/Uncore OC Mode Control"},"Enabled","Can change fabric behavior and disable sleep on some boards.","AMD","Fabric","Board-specific fabric and voltage checks required. No automatic overclocking."),
        new("Resizable BAR",new[]{"Re-Size BAR Support","Resizable BAR"},"Enabled","Can improve supported games with compatible GPU firmware and drivers; some workloads do not benefit. Available only with Above 4G decoding enabled and CSM disabled. Enabling this preference does not prove an active large memory window. Elite does not modify boot settings.",Category:"Gaming performance"),
        new("XMP / EXPO memory profile",new[]{"EXPO","Extreme Memory Profile (X.M.P.)"},"Profile 1","Requires compatible memory, current BIOS and a memory stability test.",Category:"Memory",Block:"Use the motherboard BIOS. Memory training, firmware age and voltage checks are required."),
        new("XHCI hand-off",new[]{"XHCI Hand-off","XHCI Hand-Off"},"Disabled","Changes USB controller ownership during boot.",Category:"USB",Block:"Keep unchanged until USB controller and firmware behavior are verified for the board."),
        new("Intel Software Guard Extensions",new[]{"Software Guard Extensions (SGX)","Intel SGX"},"Disabled","Disabling SGX affects applications that depend on protected enclaves.","Intel","Security","Security feature kept unchanged in the gaming preset."),
        new("Intel CPU virtualization",new[]{"Intel (VMX) Virtualization Technology","Intel Virtualization Technology"},"Disabled","CPU virtualization supports Memory Integrity, virtual machines and some anti-cheat configurations.","Intel","Virtualization","Preserved for gaming and security compatibility."),
        new("Intel VT-d",new[]{"VT-d"},"Disabled","IO virtualization supports DMA protection and anti-cheat requirements.","Intel","Virtualization","Preserved for gaming and security compatibility."),
        new("AMD SVM",new[]{"SVM Mode","SVM"},"Disabled","CPU virtualization supports Memory Integrity and virtual machines.","AMD","Virtualization","Preserved for gaming and security compatibility."),
        new("AMD IOMMU",new[]{"IOMMU"},"Disabled","IO virtualization supports DMA protection and anti-cheat requirements.","AMD","Virtualization","Preserved for gaming and security compatibility."),
        new("AMD memory encryption",new[]{"TSME","SMEE","Transparent SME"},"Disabled","Changes memory security behavior; performance benefit is not guaranteed.","AMD","Security","Security feature kept unchanged in the gaming preset."),
        new("PCIe clock power management",new[]{"Clock Power Management(CLKREQ#)"},"Disabled","Keeps PCIe clock request power management off. May increase idle power; compare device stability and latency before keeping it.",Category:"PCIe"),
        new("PCIe L1 substates",new[]{"PM L1 SS"},"Disabled","Disables this exposed PCIe L1 substate preference. May increase idle power; test NVMe, GPU, networking and sleep after reboot.",Category:"PCIe"),
        new("AMD PCIe power management",new[]{"PCIe Power Management Features"},"Disable","Disables this firmware PCIe power-management preference. May increase power consumption; restore if devices or sleep behave worse.","AMD","PCIe"),
        new("Typical-current CPU idle",new[]{"Power Supply Idle Control"},"Typical Current Idle","Useful for diagnosing low-current idle resets on affected AMD desktops. This is a stability option, not an FPS boost; may increase idle power.","AMD","CPU"),
        new("Enable CPPC",new[]{"CPPC"},"Enabled","Allows collaborative CPU performance control. Windows still chooses performance levels; benchmark and restore if behavior worsens.","AMD","Gaming performance"),
        new("Enable CPPC preferred cores",new[]{"CPPC Preferred Cores"},"Enabled","Exposes preferred-core hints to the operating system. Do not override driver-directed core selection on multi-CCD X3D systems.","AMD","Gaming performance")
    };
    private readonly string root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Quantix","Elite","BIOS");
    public string ToolPath {get;set;}="";
    public string Hardware {get;private set;}="";
    public string Snapshot {get;private set;}="";
    public string CpuVendor {get;private set;}="";
    public string CpuName {get;private set;}="";
    public BiosQuestion[] Questions => Parse(Snapshot);
    public string BackupFolder => root;
    private static readonly Regex Start=new(@"(?m)^Setup Question[\t ]*=[\t ]*(.+)\r?$",RegexOptions.CultureInvariant);
    public static BiosQuestion[] Parse(string text) {
        var result=new List<BiosQuestion>(); var starts=Start.Matches(text);
        for(int i=0;i<starts.Count;i++) {
            var start=starts[i]; int end=i+1<starts.Count?starts[i+1].Index:text.Length;
            string block=text.Substring(start.Index,end-start.Index);
            string Field(string name) => Regex.Match(block,@"(?m)^"+name+@"[\t ]*=[\t ]*([0-9a-fA-F]+)\b").Groups[1].Value;
            string guid=Regex.Matches(text.Substring(0,start.Index),@"(?m)^// GUID[\t ]*=[\t ]*([0-9a-fA-F-]{36})").Cast<Match>().LastOrDefault()?.Groups[1].Value ?? "";
            var options=Regex.Matches(block,@"(?m)^(?:Options[\t ]*=[\t ]*|[\t ]+)(\*?)\[([0-9a-fA-F]+)\]([^\r\n]+)").Cast<Match>().Select(m=>new BiosOption(m.Groups[2].Value.ToUpperInvariant(),m.Groups[3].Value.Split(new[]{"//"},StringSplitOptions.None)[0].Trim(),m.Groups[1].Value=="*")).ToArray();
            if(Field("Token").Length==0 || Field("Offset").Length==0 || Field("Width").Length==0 || options.Length<2 || options.Count(o=>o.Selected)!=1 || options.Select(o=>o.Value).Distinct().Count()!=options.Length) continue;
            string scope=guid.Length>0?guid:"flat:"+start.Groups[1].Value.Trim();
            result.Add(new(scope+":"+Field("Token")+":"+Field("Offset")+":"+Field("Width"),start.Groups[1].Value.Trim(),block,start.Index,end-start.Index,options));
        }
        return result.ToArray();
    }
    public static BiosQuestion[] ResolveMany(BiosQuestion[] questions,BiosPreset preset,string vendor="") {
        if(preset.Block.Length>0 || (preset.Vendor!="Any" && preset.Vendor!=vendor))return Array.Empty<BiosQuestion>();
        if(preset.Title=="Resizable BAR" && !BarPrerequisites(questions))return Array.Empty<BiosQuestion>();
        var matches=questions.Where(q=>preset.Names.Contains(q.Name,StringComparer.OrdinalIgnoreCase)).ToArray();
        if(matches.Length==0 || (!preset.Group && matches.Length!=1))return Array.Empty<BiosQuestion>();
        if(matches.Any(q=>questions.Count(a=>a.Key==q.Key)!=1 || q.Options.Count(o=>o.Label.Equals(preset.Target,StringComparison.OrdinalIgnoreCase))!=1))return Array.Empty<BiosQuestion>();
        return matches;
    }
    public static BiosQuestion? Resolve(BiosQuestion[] questions,BiosPreset preset,string vendor="") => ResolveMany(questions,preset,vendor).FirstOrDefault();
    public static bool BarPrerequisites(BiosQuestion[] questions) {
        bool Selected(string[] names,string target) {
            var rows=questions.Where(q=>names.Contains(q.Name,StringComparer.OrdinalIgnoreCase)).ToArray();
            return rows.Length==1 && questions.Count(q=>q.Key==rows[0].Key)==1 && rows[0].Options.Single(o=>o.Selected).Label.Equals(target,StringComparison.OrdinalIgnoreCase);
        }
        return Selected(new[]{"Above 4G Decoding","Above 4G memory/Crypto Currency mining"},"Enabled") && Selected(new[]{"CSM Support"},"Disabled");
    }
    public static string Patch(string text,IReadOnlyDictionary<string,string> choices) {
        var questions=Parse(text);
        foreach(var pair in choices) {
            var match=questions.Where(q=>q.Key==pair.Key).ToArray();
            if(match.Length!=1 || match[0].Options.Count(o=>o.Value==pair.Value)!=1)throw new InvalidOperationException("Ambiguous or unsupported firmware option.");
        }
        foreach(var q in questions.Where(q=>choices.ContainsKey(q.Key)).OrderByDescending(q=>q.Offset)) {
            string block=Regex.Replace(q.Block,@"(?m)^((?:Options[\t ]*=[\t ]*|[\t ]+))\*?\[([0-9a-fA-F]+)\]",m=>m.Groups[1].Value+(m.Groups[2].Value.Equals(choices[q.Key],StringComparison.OrdinalIgnoreCase)?"*":"")+"["+m.Groups[2].Value+"]");
            text=text.Substring(0,q.Offset)+block+text.Substring(q.Offset+q.Length);
        }
        return text;
    }
    private static string Inventory() {
        var parts=new List<string>();
        foreach(var query in new[]{"SELECT Manufacturer,Product,SerialNumber FROM Win32_BaseBoard","SELECT Manufacturer,SMBIOSBIOSVersion,ReleaseDate FROM Win32_BIOS"}) {
            using var search=new ManagementObjectSearcher(query);
            using var rows=search.Get(); foreach(ManagementObject row in rows) {using(row)foreach(PropertyData p in row.Properties)parts.Add(p.Name+"="+p.Value);}
        }
        if(parts.Count<6 || !parts.Any(p=>p.Contains("American Megatrends",StringComparison.OrdinalIgnoreCase)||p.Contains("AMI",StringComparison.OrdinalIgnoreCase)))throw new InvalidOperationException("AMI firmware identification unavailable. SCEWIN integration is unavailable for this system.");
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("|",parts.OrderBy(p=>p)))));
    }
    private async Task ValidateToolAsync() {
        if(!AdminHelper.IsRunningAsAdministrator())throw new InvalidOperationException("Restart Elite as administrator to use SCEWIN.");
        if(!File.Exists(ToolPath) || !Path.GetFileName(ToolPath).Equals("SCEWIN_64.exe",StringComparison.OrdinalIgnoreCase))throw new IOException("Choose SCEWIN_64.exe from the compatible package.");
        string folder=Path.GetDirectoryName(ToolPath)!;
        foreach(string name in new[]{"SCEWIN_64.exe","amifldrv64.sys","amigendrv64.sys"}) {
            string file=Path.Combine(folder,name);if(!File.Exists(file))throw new IOException("Missing "+name);
            string command="$s=Get-AuthenticodeSignature -LiteralPath '"+file.Replace("'","''")+"'; if($s.Status -ne 'Valid' -or $s.SignerCertificate.Subject -notmatch 'American Megatrends|AMI US HOLDINGS'){exit 2}";
            var info=new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"WindowsPowerShell","v1.0","powershell.exe")){UseShellExecute=false,CreateNoWindow=true};
            info.ArgumentList.Add("-NoProfile"); info.ArgumentList.Add("-NonInteractive");info.ArgumentList.Add("-EncodedCommand");info.ArgumentList.Add(Convert.ToBase64String(Encoding.Unicode.GetBytes(command)));
            using var p=Process.Start(info)!;await p.WaitForExitAsync();if(p.ExitCode!=0)throw new IOException("AMI signature validation failed for "+name+". Nothing executed.");
        }
    }
    private async Task RunAsync(params string[] args) {
        await ValidateToolAsync();
        var start=new ProcessStartInfo(ToolPath){WorkingDirectory=Path.GetDirectoryName(ToolPath)!,UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
        foreach(var arg in args)start.ArgumentList.Add(arg);
        using var p=Process.Start(start)!;var stdout=p.StandardOutput.ReadToEndAsync();var stderr=p.StandardError.ReadToEndAsync();await p.WaitForExitAsync();string output=await stdout+"\n"+await stderr;
        Directory.CreateDirectory(root);File.WriteAllText(Path.Combine(root,"last-operation.log"),output);
        if(!CleanOperation(p.ExitCode,output,args.FirstOrDefault()=="/o"))throw new IOException("BIOS tool did not complete cleanly. No compatibility bypass was attempted. "+output.Trim());
    }
    public static bool CleanOperation(int exitCode,string output,bool exporting) {
        if(exitCode!=0)return false;
        string checkedOutput=output;
        if(exporting && Regex.IsMatch(output,@"(?im)^\s*Script file exported successfully\.\s*$"))
            checkedOutput=Regex.Replace(checkedOutput,@"(?im)^\s*WARNING: Duplicate questions found\. Only one question will be enabled in the script\.\s*$","");
        return !Regex.IsMatch(checkedOutput,@"\b(error|warning|failed|unable)\b",RegexOptions.IgnoreCase);
    }
    private async Task<string> ExportFreshAsync() {
        using(var search=new ManagementObjectSearcher("SELECT Manufacturer,Name FROM Win32_Processor"))using(var cpus=search.Get()) {
            var models=new List<string>();var vendors=new List<string>();foreach(ManagementObject cpu in cpus)using(cpu){models.Add(cpu["Name"]?.ToString()??"");string maker=cpu["Manufacturer"]?.ToString()??"";vendors.Add(maker.Contains("Intel",StringComparison.OrdinalIgnoreCase)?"Intel":maker.Contains("AMD",StringComparison.OrdinalIgnoreCase)?"AMD":"Unknown");}
            CpuName=string.Join(" / ",models);CpuVendor=vendors.Count>0 && vendors.Distinct().Count()==1?vendors[0]:"Unknown";
        }
        string identity=Inventory();if(Hardware.Length>0 && identity!=Hardware)throw new IOException("Hardware or BIOS changed. Reload before making changes.");Hardware=identity;
        Directory.CreateDirectory(root);string path=Path.Combine(root,"export-"+Guid.NewGuid().ToString("N")+".txt");
        await RunAsync("/o","/s",path);
        if(!File.Exists(path))throw new IOException("SCEWIN did not create an export.");
        string text=Encoding.Latin1.GetString(File.ReadAllBytes(path));
        if(!Regex.IsMatch(text,@"(?m)^HIICrc32\s*=\s*[0-9a-fA-F]+") || Parse(text).Length==0)throw new IOException("Firmware did not expose usable setup options. No tweaks available.");
        return text;
    }
    public async Task LoadAsync() {await ValidateToolAsync();Snapshot=await ExportFreshAsync();}
    private string JournalPath => Path.Combine(root,"originals-"+Hardware+".json");
    private void Save(BiosJournal journal) {
        string temp=JournalPath+"."+Guid.NewGuid().ToString("N")+".tmp";
        byte[] data=JsonSerializer.SerializeToUtf8Bytes(journal);
        using(var file=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None)){file.Write(data);file.Flush(true);}
        if(File.Exists(JournalPath))File.Move(temp,JournalPath,true);else File.Move(temp,JournalPath);
    }
    public static Dictionary<string,string> Review(string snapshot,string proposed,string vendor="") {
        var before=Parse(snapshot); var after=Parse(proposed);var changes=new Dictionary<string,string>();
        if(before.Length==0 || before.Length!=after.Length || !before.Select(q=>q.Key).SequenceEqual(after.Select(q=>q.Key)))throw new IOException("Import does not match this firmware export.");
        foreach(var q in before) {
            var matches=after.Where(a=>a.Key==q.Key).ToArray();if(matches.Length!=1)throw new IOException("Duplicate firmware keys.");
            string target=matches[0].Options.Single(o=>o.Selected).Value;
            if(target==q.Options.Single(o=>o.Selected).Value)continue;
            if(!Presets.Any(p=>ResolveMany(before,p,vendor).Any(a=>a.Key==q.Key)))throw new IOException("Import changes a setting outside the supported experiment list for this CPU.");
            changes.Add(q.Key,target);
        }
        if(Patch(snapshot,changes)!=proposed)throw new IOException("Import contains extra edits. Only exposed preset option selections may change.");
        return changes;
    }
    public async Task<string> ApplyAsync(string proposed,bool restoring=false,string? restoreKey=null,string[]? restoreKeys=null) {
        if(Snapshot.Length==0)throw new IOException("Load this PC's BIOS first.");
        if(!AdminHelper.IsRunningAsAdministrator())throw new IOException("Restart Elite as administrator to use SCEWIN.");
        string lockFolder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),"Quantix","BIOS");
        Directory.CreateDirectory(root);Directory.CreateDirectory(lockFolder);using var fileLock=new FileStream(Path.Combine(lockFolder,"firmware.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
        Review(Snapshot,proposed,CpuVendor);
        if(restoring && proposed!=(restoreKeys!=null?RestoreGroupProposal(restoreKeys):RestoreProposal(restoreKey)))throw new IOException("Restore proposal changed. Reload and retry.");
        string current=await ExportFreshAsync(); var before=Parse(Snapshot);var live=Parse(current);var after=Parse(proposed);
        if(before.Length!=after.Length || !before.Select(q=>q.Key).SequenceEqual(after.Select(q=>q.Key)))throw new IOException("Import does not match this firmware export.");
        var changes=new Dictionary<string,string>();
        foreach(var q in before) {
            var changed=after.SingleOrDefault(a=>a.Key==q.Key)??throw new IOException("Duplicate firmware keys.");
            string original=q.Options.Single(o=>o.Selected).Value;string target=changed.Options.Single(o=>o.Selected).Value;
            if(original==target)continue;
            if(!Presets.Any(p=>ResolveMany(before,p,CpuVendor).Any(a=>a.Key==q.Key)))throw new IOException("Import changes a setting outside the supported experiment list for this CPU.");
            var present=live.Where(a=>a.Key==q.Key).ToArray();if(present.Length!=1 || present[0].Options.Single(o=>o.Selected).Value!=original){Snapshot=current;throw new IOException("Firmware state changed since the last read. Switches refreshed from the current BIOS; review the new state before retrying.");}
            changes.Add(q.Key,target);
        }
        if(Patch(Snapshot,changes)!=proposed)throw new IOException("Import contains extra edits. Only the exposed preset option selections may change.");
        if(changes.Count==0)return "No BIOS changes needed. Saved original values remain available.";
        var journal=File.Exists(JournalPath)?JsonSerializer.Deserialize<BiosJournal>(File.ReadAllText(JournalPath))!:new BiosJournal(Hardware,new());
        if(journal.Hardware!=Hardware)throw new IOException("Backup belongs to different hardware/BIOS.");
        foreach(var pair in changes) {
            var q=before.Single(q=>q.Key==pair.Key);var saved=journal.Entries.SingleOrDefault(e=>e.Key==q.Key);
            if(saved==null)journal.Entries.Add(new(q.Key,q.Name,q.Options.Single(o=>o.Selected).Value,pair.Value));
            else if(!restoring) {journal.Entries.Remove(saved);journal.Entries.Add(saved with{Target=pair.Value,Previous=q.Options.Single(o=>o.Selected).Value});}
        }
        Save(journal); // Durable original values before importing, including failures.
        string targetPath=Path.Combine(root,"apply-"+Guid.NewGuid().ToString("N")+".txt");File.WriteAllBytes(targetPath,Encoding.Latin1.GetBytes(Patch(current,changes)));
        await RunAsync("/i","/s",targetPath);
        Snapshot=await ExportFreshAsync(); var verified=Parse(Snapshot);
        bool matches=changes.All(c=>verified.Count(q=>q.Key==c.Key && q.Options.Single(o=>o.Selected).Value==c.Value)==1);
        if(restoring && matches){journal.Entries.RemoveAll(e=>changes.ContainsKey(e.Key));Save(journal);}
        return matches?"Requested BIOS values read back successfully. Restart Windows, then reload to check after boot. Originals remain available for restore.":"Import submitted, but requested values did not read back. Original backup retained. Reload after reboot; do not assume tweaks applied.";
    }
    public bool HasOriginal(string key) {
        if(!File.Exists(JournalPath))return false;
        var journal=JsonSerializer.Deserialize<BiosJournal>(File.ReadAllText(JournalPath))!;
        if(journal.Hardware!=Hardware)throw new IOException("Backup hardware mismatch.");
        return journal.Entries.Any(e=>e.Key==key);
    }
    public string RestoreGroupProposal(string[] keys) {
        if(!File.Exists(JournalPath))throw new IOException("No saved BIOS originals for this hardware.");
        var journal=JsonSerializer.Deserialize<BiosJournal>(File.ReadAllText(JournalPath))!;
        if(journal.Hardware!=Hardware)throw new IOException("Backup hardware mismatch.");
        var entries=journal.Entries.Where(e=>keys.Contains(e.Key)).ToList();
        if(entries.Count==0)throw new IOException("No saved originals for this control.");
        return RestoreSnapshot(Snapshot,new BiosJournal(journal.Hardware,entries));
    }
    public string RestoreProposal(string? key=null) {
        if(!File.Exists(JournalPath))throw new IOException("No saved BIOS changes for this board and BIOS version.");
        var journal=JsonSerializer.Deserialize<BiosJournal>(File.ReadAllText(JournalPath))!;
        if(journal.Hardware!=Hardware)throw new IOException("Backup hardware mismatch.");
        return RestoreSnapshot(Snapshot,journal,key);
    }
    public static string RestoreSnapshot(string snapshot,BiosJournal journal,string? key=null) {
        if(key!=null){journal=new BiosJournal(journal.Hardware,journal.Entries.Where(e=>e.Key==key).ToList());if(journal.Entries.Count!=1)throw new IOException("No original value saved for this tweak. Elite cannot invent a previous BIOS value.");}
        var questions=Parse(snapshot);
        foreach(var entry in journal.Entries) {
            var matches=questions.Where(q=>q.Key==entry.Key).ToArray();
            if(matches.Length!=1)throw new IOException("Saved setting is missing or ambiguous. Restore is blocked.");
            string current=matches[0].Options.Single(o=>o.Selected).Value;
            if(current!=entry.Target && current!=entry.Original && current!=entry.Previous)throw new IOException("A saved BIOS setting changed outside Elite. Restore is blocked to preserve that change.");
        }
        return Patch(snapshot,journal.Entries.ToDictionary(e=>e.Key,e=>e.Original));
    }
}

