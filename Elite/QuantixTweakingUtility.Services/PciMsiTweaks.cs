using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using QuantixTweakingUtility.Models;

namespace QuantixTweakingUtility.Services;

public enum PciMsiKind { Network, Usb, Nvme }
public record PciMsiOriginal(string Id,string Driver,int? Value);
public static class PciMsiTweaks
{
    public static string PathFor(string id)
    {
        if(id==null||id.Contains("..")||!Regex.IsMatch(id,@"\APCI\\VEN_[0-9A-F]{4}&DEV_[0-9A-F]{4}[^\\:/\x00-\x1F]*\\[^\\:/\x00-\x1F]+\z",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant))
            throw new IOException("Invalid PCI device identity.");
        return @"SYSTEM\CurrentControlSet\Enum\"+id+@"\Device Parameters\Interrupt Management\MessageSignaledInterruptProperties";
    }
    public static async Task<GpuInterruptDevice[]> Discover(PciMsiKind kind)
    {
        string selection=kind switch {
            PciMsiKind.Network=>"$ids=@(Get-NetAdapter -Physical -ErrorAction Stop|Where-Object {[int]$_.NdisPhysicalMedium -eq 14}|ForEach-Object {[string]$_.PnPDeviceID});$devices=@(Get-PnpDevice -Class Net -PresentOnly -ErrorAction Stop|Where-Object {$ids -contains $_.InstanceId})",
            PciMsiKind.Usb=>"$devices=@(Get-PnpDevice -Class USB -PresentOnly -ErrorAction Stop|Where-Object {$_.InstanceId -like 'PCI\\*'}|Where-Object {@((Get-PnpDeviceProperty -InstanceId $_.InstanceId -KeyName DEVPKEY_Device_CompatibleIds -ErrorAction Stop).Data|Where-Object {$_ -like 'PCI\\CC_0C03*'}).Count -gt 0})",
            PciMsiKind.Nvme=>"$devices=@(Get-PnpDevice -Class SCSIAdapter -PresentOnly -ErrorAction Stop|Where-Object {$_.InstanceId -like 'PCI\\*'}|Where-Object {@((Get-PnpDeviceProperty -InstanceId $_.InstanceId -KeyName DEVPKEY_Device_CompatibleIds -ErrorAction Stop).Data|Where-Object {$_ -eq 'PCI\\CC_010802'}).Count -gt 0})",
            _=>throw new IOException("Unknown interrupt device group.")
        };
        string script=selection+";$rows=@($devices|ForEach-Object {$device=$_;$support=Get-PnpDeviceProperty -InstanceId $device.InstanceId -KeyName DEVPKEY_PciDevice_InterruptSupport -ErrorAction Stop;$driver=Get-PnpDeviceProperty -InstanceId $device.InstanceId -KeyName DEVPKEY_Device_DriverVersion -ErrorAction Stop;[pscustomobject]@{Id=[string]$device.InstanceId;Name=[string]$device.FriendlyName;Support=[uint32]$support.Data;Driver=[string]$driver.Data}});ConvertTo-Json -InputObject $rows -Depth 3 -Compress";
        return JsonSerializer.Deserialize<GpuInterruptDevice[]>(await WindowsAdapterRssStore.Run(script)) ?? throw new IOException("PCI interrupt inspection unavailable.");
    }
    public static TweakDefinition Build(PciMsiKind kind,IRestoreService restore,IGpuMsiStore? store=null,Func<Task<GpuInterruptDevice[]>>? discover=null)
    {
        store??=new GpuMsiStore();discover??=()=>Discover(kind);var gate=new SemaphoreSlim(1,1);
        string id=kind switch{PciMsiKind.Network=>"network.pci-msi.enable",PciMsiKind.Usb=>"input.usb-controller-msi.enable",PciMsiKind.Nvme=>"storage.nvme-msi.enable",_=>throw new IOException("Unknown interrupt group.")};
        string name=kind switch{PciMsiKind.Network=>"NIC MSI Mode",PciMsiKind.Usb=>"USB Controller MSI Mode",_=>"NVMe MSI Mode"};
        string scope=kind switch{PciMsiKind.Network=>"present physical wired PCI network adapters",PciMsiKind.Usb=>"present PCI USB host controllers",_=>"present PCI NVMe controllers"};
        string consequence=kind switch{PciMsiKind.Network=>"A driver incompatibility can disrupt network access.",PciMsiKind.Usb=>"A driver incompatibility can disable attached USB input or other devices.",_=>"A storage-driver incompatibility can cause drive access or boot problems; this is especially consequential on a system-drive controller."};
        return new TweakDefinition{
            Id=id,Name=name,Category=kind==PciMsiKind.Network?TweakCategory.Network:kind==PciMsiKind.Usb?TweakCategory.Input:TweakCategory.Storage,
            RiskLevel=TweakRiskLevel.High,RequiresAdministrator=true,RequiresRestart=true,HasSavedOriginal=()=>restore.GetBackup(id)!=null,
            Description="Requests MSISupported=1 on "+scope+" only when every target reports PCI MSI capability. Hardware capability alone does not prove driver compatibility or the active interrupt mode. "+consequence+" This does not guarantee lower latency. Elite saves each device's exact original DWORD or absence, tied to its driver version. Restart Windows after applying or restoring. Changed/missing devices retain their originals for review.",
            AffectedSettings=new[]{"PCI:"+kind+":MSISupported"},
            CurrentStateReader=async()=>{try{var devices=await Inspect(restore.GetBackup(id)==null);if(devices.Length==0)return TweakState.Unsupported;return devices.All(d=>Read(d)==1)?TweakState.Applied:TweakState.NotApplied;}catch(IOException){return TweakState.Unsupported;}},
            ApplyAction=()=>Change(true),RestoreAction=()=>Change(false)
        };
        int? Read(GpuInterruptDevice device){int? value=store.Read(PathFor(device.Id));if(value is not null and not 0 and not 1)throw new IOException("Unsupported MSI registry value; original retained.");return value;}
        async Task<GpuInterruptDevice[]> Inspect(bool capability){
            var devices=await discover();foreach(var device in devices){if(device==null)throw new IOException("PCI device identity unavailable.");_=PathFor(device.Id);if(string.IsNullOrWhiteSpace(device.Driver))throw new IOException("PCI driver identity unavailable.");}
            if(devices.Select(d=>d.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=devices.Length)throw new IOException("Ambiguous PCI identities.");
            if(capability&&(devices.Length==0||devices.Any(d=>(d.Support&2)==0)))throw new IOException("Every target must report PCI MSI support before applying this group.");return devices;
        }
        async Task<TweakActionResult> Change(bool apply){
            await gate.WaitAsync();try{
                var devices=await Inspect(apply);string? raw=restore.GetBackup(id);PciMsiOriginal[]? originals;
                try{originals=raw==null?null:JsonSerializer.Deserialize<PciMsiOriginal[]>(raw);}catch(JsonException ex){throw new IOException("Unreadable PCI MSI originals; no values changed.",ex);}
                if(raw!=null&&(originals==null||originals.Length==0))throw new IOException("Invalid PCI MSI originals.");
                if(originals!=null){foreach(var original in originals){if(original==null)throw new IOException("Invalid saved PCI identity.");_=PathFor(original.Id);if(string.IsNullOrWhiteSpace(original.Driver)||original.Value is not null and not 0 and not 1)throw new IOException("Invalid saved interrupt state.");}if(originals.Select(o=>o.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=originals.Length)throw new IOException("Duplicate saved PCI identities.");}
                if(!apply&&originals==null)return TweakActionResult.Fail("No original interrupt values were saved by Elite.");
                if(apply&&originals!=null&&!SameSet(devices.Select(d=>d.Id),originals.Select(o=>o.Id)))throw new IOException("The target device set changed. Restore recorded originals before applying again.");
                if(originals==null){originals=devices.Select(d=>new PciMsiOriginal(d.Id,d.Driver,Read(d))).ToArray();raw=JsonSerializer.Serialize(originals);restore.SaveBackup(id,raw);if(restore.GetBackup(id)!=raw)throw new IOException("Interrupt backup verification failed; no values changed.");}
                var errors=new List<string>();
                foreach(var original in originals){try{
                    var live=(await Inspect(false)).SingleOrDefault(d=>d.Id.Equals(original.Id,StringComparison.OrdinalIgnoreCase)) ?? throw new IOException("Recorded PCI device is missing.");
                    if(live.Driver!=original.Driver)throw new IOException("Driver changed; original retained for review.");
                    if(apply&&(live.Support&2)==0)throw new IOException("PCI MSI capability is no longer reported.");
                    int? current=Read(live);if(current!=1&&current!=original.Value)throw new IOException("Interrupt value changed outside Elite.");
                    int? target=apply?1:original.Value;if(current!=target)store.Write(PathFor(live.Id),target);
                    if(Read(live)!=target)throw new IOException("Interrupt registry readback failed.");
                    var after=(await Inspect(false)).SingleOrDefault(d=>d.Id.Equals(original.Id,StringComparison.OrdinalIgnoreCase));
                    if(after==null||after.Driver!=original.Driver)throw new IOException("Device or driver changed during verification; original retained.");
                }catch(IOException ex){errors.Add(original.Id+": "+ex.Message);}}
                if(errors.Count>0)return TweakActionResult.Fail("Some interrupt values could not be "+(apply?"applied":"restored")+". Originals retained. "+string.Join(" ",errors));
                if(apply&&!SameSet((await Inspect(true)).Select(d=>d.Id),originals.Select(o=>o.Id)))throw new IOException("The device set changed during application; originals retained.");
                if(!apply)restore.ClearBackup(id);
                return TweakActionResult.Ok(name+(apply?" registry configuration applied":" original values restored")+" and verified. Restart Windows; active interrupt mode has not been measured.");
            }finally{gate.Release();}
        }
    }
    private static bool SameSet(IEnumerable<string> a,IEnumerable<string> b)=>a.OrderBy(s=>s,StringComparer.OrdinalIgnoreCase).SequenceEqual(b.OrderBy(s=>s,StringComparer.OrdinalIgnoreCase),StringComparer.OrdinalIgnoreCase);
}
