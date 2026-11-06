using System;
using System.IO;
using Microsoft.Win32;
namespace QuantixTweakingUtility.Services;
// MPO is a Windows DWM setting, not an NVAPI setting ID. Keep this route explicit.
public sealed class NvidiaControlDriver:INvidiaSettings {
 private readonly NativeNvidiaSettings native=new();
 private const string Path="SOFTWARE\\Microsoft\\Windows\\Dwm",Name="OverlayTestMode";
 public NvidiaValue Read(uint id){if(id!=NvidiaSettingsService.MpoId)return native.Read(id);using var key=Registry.LocalMachine.OpenSubKey(Path);var value=key?.GetValue(Name);if(value==null)return new(0,true);if(key!.GetValueKind(Name)!=RegistryValueKind.DWord)throw new IOException("Unexpected MPO value type.");return new(unchecked((uint)(int)value),false);}
 private static void Admin(){if(!AdminHelper.IsRunningAsAdministrator())throw new IOException("Run Elite as administrator to change or restore MPO.");}
 public void Write(uint id,uint value){if(id!=NvidiaSettingsService.MpoId){native.Write(id,value);return;}Admin();using var key=Registry.LocalMachine.CreateSubKey(Path,true);key.SetValue(Name,unchecked((int)value),RegistryValueKind.DWord);}
 public void Delete(uint id){if(id!=NvidiaSettingsService.MpoId){native.Delete(id);return;}Admin();using var key=Registry.LocalMachine.OpenSubKey(Path,true);key?.DeleteValue(Name,false);}
}
