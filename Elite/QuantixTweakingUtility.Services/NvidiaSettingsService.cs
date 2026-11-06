using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
namespace QuantixTweakingUtility.Services;
public record NvidiaOption(uint Id,uint Target,string Title,string Detail);
public record NvidiaValue(uint Value,bool Predefined);
public interface INvidiaSettings { NvidiaValue Read(uint id); void Write(uint id,uint value); void Delete(uint id); }
public sealed class NvidiaSettingsService {
 public static readonly NvidiaOption[] LegacyOptions={
 new(0x1057EB71,1,"Prefer maximum performance","Global NVIDIA power preference. May improve clock consistency, but increases power, heat and fan use."),
 new(0x0064B541,1,"Prefer highest refresh rate","Requests the highest available refresh rate for supported applications. Does not change desktop resolution."),
 new(0x00A879CF,0x08416747,"Disable driver V-Sync","Lets supported applications render without driver V-Sync. Can reduce waiting but causes tearing; games may override it."),
 new(0x20C1221E,1,"OpenGL threaded optimization","Enables driver threading for OpenGL applications. Compare frame pacing; some games work better on Auto."),
 new(0x00E73211,1,"Anisotropic sample optimization","Reduces texture-filter sampling where supported. Can trade image quality for performance."),
 new(0x0084CD70,1,"Anisotropic filter optimization","Allows the driver's anisotropic filtering optimization. Visual quality and performance depend on the game."),
 new(0x002ECAF2,1,"Trilinear filtering optimization","Allows optimized trilinear filtering where supported. Can trade texture quality for performance.")};
 public static readonly NvidiaOption[] Options={
 new(0x10835002,0,"Remove NVIDIA driver FPS limit","Disables the global NVIDIA frame-rate limiter. Game limits and V-Sync still apply. Can increase GPU load, heat and tearing; keeping a suitable cap may give smoother pacing."),
 new(0x10835016,30,"Limit background applications to 30 FPS","Reduces rendering load while a game or 3D app is unfocused. Foreground frame rate is unchanged. Useful when switching to Discord or a browser; not an active-game FPS boost."),
 new(0x20FDD1F9,0,"Disable OpenGL triple buffering","Disables the driver's extra OpenGL back buffer. Only relevant to OpenGL/V-Sync workloads; may reduce smoothness. Does not change DirectX game buffering."),
 new(MpoId,5,"Disable MPO (flicker troubleshooting)","Optional desktop flicker troubleshooting, not a general FPS boost. Disables Windows multiplane overlays for all displays. Can increase composition load or affect presentation. Administrator and Windows restart required."),
 new(0x20D690F8,1,"Use DXGI presentation for Vulkan/OpenGL","Prefers the driver's DXGI presentation path for compatible Vulkan/OpenGL apps. Can change windowed presentation behavior; restore if capture, overlays or frame pacing worsen.")};
 public const uint MpoId=0xffff0001;
 public static readonly NvidiaOption[] CardOptions=Options.Concat(LegacyOptions.Where(option=>option.Id==0x1057EB71)).ToArray();
 public record Original(uint Id,NvidiaValue Before,uint Target);
 private readonly INvidiaSettings driver; private readonly string root;
 public NvidiaSettingsService(INvidiaSettings? driver=null,string? root=null){this.driver=driver??new NvidiaControlDriver();this.root=root??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Quantix","Elite","Nvidia");}
 private string FilePath=>Path.Combine(root,"originals.json");
 private List<Original> Load()=>File.Exists(FilePath)?JsonSerializer.Deserialize<List<Original>>(File.ReadAllText(FilePath))??throw new IOException("Invalid NVIDIA backup."):new();
 private void Save(List<Original> entries){var temp=FilePath+".tmp";using(var f=new FileStream(temp,FileMode.Create,FileAccess.Write,FileShare.None)){JsonSerializer.Serialize(f,entries);f.Flush(true);}File.Move(temp,FilePath,true);}
 public NvidiaValue Read(uint id)=>driver.Read(id);
 public bool HasOriginal(uint id)=>Load().Any(x=>x.Id==id);
 public void Change(uint id,bool on){var option=Options.Concat(LegacyOptions).First(x=>x.Id==id);Directory.CreateDirectory(root);using var gate=new FileStream(Path.Combine(root,"operation.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);var entries=Load();var saved=entries.SingleOrDefault(x=>x.Id==id);var current=driver.Read(id);
 if(on){if(saved!=null&&current.Value!=saved.Target&&current!=saved.Before)throw new IOException("This setting changed outside Elite. Original backup retained.");if(saved==null){entries.Add(new(id,current,option.Target));Save(entries);}driver.Write(id,option.Target);if(driver.Read(id).Value!=option.Target)throw new IOException("NVIDIA readback did not match. Backup retained.");}
 else{if(saved==null)throw new IOException("No original value saved for this setting.");if(current.Value!=saved.Target&&current!=saved.Before)throw new IOException("This setting changed outside Elite. Restore stopped.");if(saved.Before.Predefined)driver.Delete(id);else driver.Write(id,saved.Before.Value);if(driver.Read(id)!=saved.Before)throw new IOException("Restore readback did not match. Backup retained.");entries.Remove(saved);Save(entries);}}
}
public sealed class NativeNvidiaSettings : INvidiaSettings {
 [DefaultDllImportSearchPaths(DllImportSearchPath.System32)] [DllImport("nvapi64.dll",EntryPoint="nvapi_QueryInterface",CallingConvention=CallingConvention.Cdecl)] private static extern IntPtr Query(uint id);
 [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Init();
 [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Create(out IntPtr session);
 [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Session(IntPtr session);
 [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Base(IntPtr session,out IntPtr profile);
 [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Get(IntPtr session,IntPtr profile,uint id,IntPtr value);
 [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Set(IntPtr session,IntPtr profile,IntPtr value);
 [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int DeleteSetting(IntPtr session,IntPtr profile,uint id);
 private static T Api<T>(uint id) where T:Delegate{var ptr=Query(id);if(ptr==IntPtr.Zero)throw new IOException("NVIDIA driver does not support this control.");return Marshal.GetDelegateForFunctionPointer<T>(ptr);}
 private static void Check(int code){if(code!=0)throw new IOException("NVIDIA driver rejected the operation ("+code+").");}
 private T WithSession<T>(Func<IntPtr,IntPtr,T> action){Check(Api<Init>(0x0150e828)());Check(Api<Create>(0x0694d52e)(out var session));try{Check(Api<Session>(0x375dbd6b)(session));Check(Api<Base>(0xda8466a0)(session,out var profile));return action(session,profile);}finally{Api<Session>(0xdad9cff8)(session);}}
 // NVDRS_SETTING_V1 uses pack(4), a 2048 UTF-16 name, and two 4100-byte unions.
 private static IntPtr Buffer(uint id){var ptr=Marshal.AllocHGlobal(12320);Marshal.Copy(new byte[12320],0,ptr,12320);Marshal.WriteInt32(ptr,12320|(1<<16));Marshal.WriteInt32(ptr,4100,unchecked((int)id));return ptr;}
 public NvidiaValue Read(uint id)=>WithSession((s,p)=>{var b=Buffer(id);try{Check(Api<Get>(0x73bf8338)(s,p,id,b));if(Marshal.ReadInt32(b,4104)!=0)throw new IOException("Unsupported NVIDIA setting type.");return new NvidiaValue(unchecked((uint)Marshal.ReadInt32(b,8220)),Marshal.ReadInt32(b,4112)!=0);}finally{Marshal.FreeHGlobal(b);}});
 public void Write(uint id,uint value)=>WithSession((s,p)=>{var b=Buffer(id);try{Marshal.WriteInt32(b,8220,unchecked((int)value));Check(Api<Set>(0x577dd202)(s,p,b));Check(Api<Session>(0xfcbc7e14)(s));return true;}finally{Marshal.FreeHGlobal(b);}});
 public void Delete(uint id)=>WithSession((s,p)=>{Check(Api<DeleteSetting>(0xe4a26362)(s,p,id));Check(Api<Session>(0xfcbc7e14)(s));return true;});
}



