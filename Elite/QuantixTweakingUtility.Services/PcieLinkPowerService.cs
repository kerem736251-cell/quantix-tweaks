using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using QuantixTweakingUtility.Models;
namespace QuantixTweakingUtility.Services;
public interface IPcieLinkPowerStore { Guid Active(); uint Read(Guid plan); void Write(Guid plan,uint value); }
public sealed class PcieLinkPowerStore:IPcieLinkPowerStore {
 private static Guid Group=new("501a4d13-42af-4429-9fd1-a8218c268e20"), Setting=new("ee12f906-d277-404b-b6da-e5fa1a576df5");
 [DllImport("powrprof.dll")]private static extern uint PowerGetActiveScheme(IntPtr root,out IntPtr scheme);
 [DllImport("powrprof.dll")]private static extern uint PowerReadACValueIndex(IntPtr root,ref Guid scheme,ref Guid group,ref Guid setting,out uint value);
 [DllImport("powrprof.dll")]private static extern uint PowerWriteACValueIndex(IntPtr root,ref Guid scheme,ref Guid group,ref Guid setting,uint value);
 [DllImport("powrprof.dll")]private static extern uint PowerSetActiveScheme(IntPtr root,ref Guid scheme);
 [DllImport("kernel32.dll")]private static extern IntPtr LocalFree(IntPtr memory);
 private static void Check(uint code){if(code!=0)throw new IOException("Windows rejected PCIe power setting ("+code+").");}
 public Guid Active(){Check(PowerGetActiveScheme(IntPtr.Zero,out var ptr));try{return Marshal.PtrToStructure<Guid>(ptr);}finally{LocalFree(ptr);}}
 public uint Read(Guid plan){Check(PowerReadACValueIndex(IntPtr.Zero,ref plan,ref Group,ref Setting,out uint value));return value;}
 public void Write(Guid plan,uint value){if(Active()!=plan)throw new IOException("Active power plan changed. No write performed.");Check(PowerWriteACValueIndex(IntPtr.Zero,ref plan,ref Group,ref Setting,value));Check(PowerSetActiveScheme(IntPtr.Zero,ref plan));}
}
public sealed class PcieLinkPowerService {
 public TweakDefinition CreateTweak()=>new(){
  Id="power.pcie-aspm.disable",Name="PCIe ASPM Off",
  Description="Turns off PCIe Link State Power Management in the active Windows power plan while plugged in. Affects all PCIe links, including the GPU, and may increase power use and heat. Battery settings remain unchanged. Restores the saved original in the same plan; shares recovery data with Graphics tools.",
  Category=TweakCategory.Cpu,RiskLevel=TweakRiskLevel.Medium,RequiresAdministrator=true,
  HasSavedOriginal=()=>HasOriginal,
  AffectedSettings=new[]{"powercfg:AC:501a4d13-42af-4429-9fd1-a8218c268e20:ee12f906-d277-404b-b6da-e5fa1a576df5"},
  CurrentStateReader=()=>{try{return Task.FromResult(Current()==0?TweakState.Applied:TweakState.NotApplied);}catch(IOException){return Task.FromResult(TweakState.Unsupported);}},
  ApplyAction=async()=>{await Task.Run(()=>Change(true));return TweakActionResult.Ok("PCIe link power saving disabled and verified.");},
  RestoreAction=async()=>{await Task.Run(()=>Change(false));return TweakActionResult.Ok("Original PCIe link power policy restored and verified.");}
 };
 public record Original(Guid Plan,uint Value);
 private readonly IPcieLinkPowerStore store;private readonly string root;private readonly Func<bool> admin;
 public PcieLinkPowerService(IPcieLinkPowerStore? store=null,string? root=null,Func<bool>? admin=null){this.store=store??new PcieLinkPowerStore();this.root=root??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Quantix","Elite","PciePower");this.admin=admin??AdminHelper.IsRunningAsAdministrator;}
 private string Backup=>Path.Combine(root,"original.json");
 private Original? Load()=>File.Exists(Backup)?JsonSerializer.Deserialize<Original>(File.ReadAllText(Backup))??throw new IOException("Invalid power backup."):null;
 public bool HasOriginal=>Load()!=null;
 public uint Current()=>store.Read(store.Active());
 public void Change(bool on){if(!admin())throw new IOException("Run Elite as administrator to change PCIe power saving.");Directory.CreateDirectory(root);using var gate=new FileStream(Path.Combine(root,"operation.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);var plan=store.Active();var value=store.Read(plan);if(value>2)throw new IOException("Unrecognized PCIe power policy.");var saved=Load();if(saved!=null&&saved.Plan!=plan)throw new IOException("Return to the original power plan before changing or restoring this switch.");
 if(on){if(saved==null){saved=new(plan,value);using(var file=new FileStream(Backup+".tmp",FileMode.Create,FileAccess.Write,FileShare.None)){JsonSerializer.Serialize(file,saved);file.Flush(true);}File.Move(Backup+".tmp",Backup,true);}if(value!=0&&value!=saved.Value)throw new IOException("Power policy changed outside Elite.");store.Write(plan,0);if(store.Read(plan)!=0)throw new IOException("Power change verification failed. Backup retained.");}
 else{if(saved==null)throw new IOException("No original power setting saved by Elite.");if(value!=0&&value!=saved.Value)throw new IOException("Power policy changed outside Elite.");store.Write(plan,saved.Value);if(store.Read(plan)!=saved.Value)throw new IOException("Power restoration verification failed. Backup retained.");File.Delete(Backup);}}
}
