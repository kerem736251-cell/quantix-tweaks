using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Win32;
using QuantixTweakingUtility.Models;
namespace QuantixTweakingUtility.Services;
public record GpuInterruptDevice(string Id,string Name,uint Support,string Driver);
public interface IGpuMsiStore { int? Read(string path); void Write(string path,int? value); }
public sealed class GpuMsiStore:IGpuMsiStore {
 public int? Read(string path){using var key=Registry.LocalMachine.OpenSubKey(path);var value=key?.GetValue("MSISupported");if(value==null)return null;if(key!.GetValueKind("MSISupported")!=RegistryValueKind.DWord)throw new IOException("Unexpected MSI value type.");return (int)value;}
 public void Write(string path,int? value){using var key=Registry.LocalMachine.CreateSubKey(path,true);if(value.HasValue)key.SetValue("MSISupported",value.Value,RegistryValueKind.DWord);else key.DeleteValue("MSISupported",false);}
}
public sealed class GpuMsiService {
 public TweakDefinition CreateTweak()=>new(){
  Id="gpu.nvidia.msi.enable",Name="Enable GPU MSI Mode",
  Description="Requests message-signaled interrupts on present NVIDIA display adapters that report MSI support. Saves each adapter's original registry value, tied to its driver version. Restart Windows after changes. The configured value does not prove the active interrupt mode or guarantee lower latency.",
  Category=TweakCategory.Gpu,RiskLevel=TweakRiskLevel.High,RequiresAdministrator=true,RequiresRestart=true,
  HasSavedOriginal=()=>Load().Count>0,
  AffectedSettings=new[]{"PCI:NVIDIA:MSISupported"},
  CurrentStateReader=async()=>{try{var devices=await Supported();var values=devices.Select(Read).ToArray();if(values.Any(value=>value is not null and not 0 and not 1))return TweakState.Unknown;return values.All(value=>value==1)?TweakState.Applied:TweakState.NotApplied;}catch(IOException){return TweakState.Unsupported;}},
  ApplyAction=async()=>{var devices=await Supported();foreach(var device in devices)if(Read(device)!=1)await Change(device,true);return TweakActionResult.Ok("GPU MSI configuration verified. Restart Windows to apply.");},
  RestoreAction=async()=>{var devices=await discover();int count=0;foreach(var device in devices)if(HasOriginal(device)){await Change(device,false);count++;}return count>0?TweakActionResult.Ok("Saved GPU MSI values restored. Restart Windows to apply."):TweakActionResult.Fail("No original GPU MSI values were saved by Elite.");}
 };
 private async Task<GpuInterruptDevice[]> Supported(){var devices=await discover();if(devices.Length==0||devices.Any(device=>(device.Support&2)==0))throw new IOException("Every target NVIDIA adapter must report MSI support before this group can be applied.");return devices;}
 public record Original(string Id,string Driver,int? Value);
 private readonly IGpuMsiStore store;private readonly string root;private readonly Func<Task<GpuInterruptDevice[]>> discover;private readonly Func<bool> admin;
 public GpuMsiService(IGpuMsiStore? store=null,string? root=null,Func<Task<GpuInterruptDevice[]>>? discover=null,Func<bool>? admin=null){this.store=store??new GpuMsiStore();this.root=root??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Quantix","Elite","GpuInterrupts");this.discover=discover??Discover;this.admin=admin??AdminHelper.IsRunningAsAdministrator;}
 private string Backup=>Path.Combine(root,"originals.json");
 public static string PathFor(string id){if(!id.StartsWith("PCI\\VEN_10DE&",StringComparison.OrdinalIgnoreCase)||id.Count(c=>c=='\\')!=2||id.Contains("..")||id.Contains(':'))throw new IOException("Invalid NVIDIA PCI device.");return "SYSTEM\\CurrentControlSet\\Enum\\"+id+"\\Device Parameters\\Interrupt Management\\MessageSignaledInterruptProperties";}
 private List<Original> Load()=>File.Exists(Backup)?JsonSerializer.Deserialize<List<Original>>(File.ReadAllText(Backup))??throw new IOException("Invalid interrupt backup."):new();
 private void Save(List<Original> entries){var tmp=Backup+".tmp";using(var stream=new FileStream(tmp,FileMode.Create,FileAccess.Write,FileShare.None)){JsonSerializer.Serialize(stream,entries);stream.Flush(true);}File.Move(tmp,Backup,true);}
 public int? Read(GpuInterruptDevice device)=>store.Read(PathFor(device.Id));
 public bool HasOriginal(GpuInterruptDevice device)=>Load().Any(x=>x.Id==device.Id);
 public async Task Change(GpuInterruptDevice device,bool on){if(!admin())throw new IOException("Run Elite as administrator to change GPU interrupt mode.");Directory.CreateDirectory(root);using var gate=new FileStream(Path.Combine(root,"operation.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);var live=(await discover()).SingleOrDefault(x=>x.Id==device.Id)??throw new IOException("GPU is no longer present.");if(live.Driver!=device.Driver)throw new IOException("GPU driver changed. Rescan before changing interrupt mode.");if(on&&(live.Support&2)==0)throw new IOException("Windows does not report MSI support for this GPU.");var path=PathFor(live.Id);var current=store.Read(path);if(current is not null and not 0 and not 1)throw new IOException("Unexpected interrupt configuration.");var entries=Load();var original=entries.SingleOrDefault(x=>x.Id==live.Id);if(original!=null&&original.Driver!=live.Driver)throw new IOException("Saved backup belongs to a different driver. Restore stopped.");
 if(on){if(original!=null&&current!=1&&current!=original.Value)throw new IOException("Interrupt setting changed outside Elite.");if(original==null){entries.Add(new(live.Id,live.Driver,current));Save(entries);}store.Write(path,1);if(store.Read(path)!=1)throw new IOException("MSI configuration readback failed; backup retained.");}
 else{if(original==null)throw new IOException("No original interrupt value saved by Elite.");if(current!=1&&current!=original.Value)throw new IOException("Interrupt setting changed outside Elite.");store.Write(path,original.Value);if(store.Read(path)!=original.Value)throw new IOException("Restore verification failed; backup retained.");entries.Remove(original);Save(entries);}}
 public static async Task<GpuInterruptDevice[]> Discover(){const string script="$ErrorActionPreference='Stop';[Console]::OutputEncoding=[Text.UTF8Encoding]::new($false);$rows=@(Get-PnpDevice -Class Display -PresentOnly | Where-Object {$_.InstanceId -like 'PCI\\VEN_10DE*'} | ForEach-Object {$device=$_;$support=Get-PnpDeviceProperty -InstanceId $device.InstanceId -KeyName 'DEVPKEY_PciDevice_InterruptSupport';$driver=Get-PnpDeviceProperty -InstanceId $device.InstanceId -KeyName 'DEVPKEY_Device_DriverVersion';[pscustomobject]@{Id=$device.InstanceId;Name=$device.FriendlyName;Support=[uint32]$support.Data;Driver=[string]$driver.Data}});ConvertTo-Json -InputObject $rows -Compress";
 var start=new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),"System32","WindowsPowerShell","v1.0","powershell.exe")){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=Encoding.UTF8};start.ArgumentList.Add("-NoProfile");start.ArgumentList.Add("-NonInteractive");start.ArgumentList.Add("-EncodedCommand");start.ArgumentList.Add(Convert.ToBase64String(Encoding.Unicode.GetBytes(script)));using var process=Process.Start(start)??throw new IOException("Cannot read GPU interrupt support.");var output=process.StandardOutput.ReadToEndAsync();var error=process.StandardError.ReadToEndAsync();await process.WaitForExitAsync();if(process.ExitCode!=0)throw new IOException(await error);return JsonSerializer.Deserialize<GpuInterruptDevice[]>(await output)??Array.Empty<GpuInterruptDevice>();}
}
