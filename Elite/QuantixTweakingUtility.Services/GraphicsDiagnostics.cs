using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Extensions.DependencyInjection;
using QuantixTweakingUtility.Views;
using QuantixTweakingUtility.ViewModels;
namespace QuantixTweakingUtility.Services;
public static class GraphicsDiagnostics {
    private sealed class Store:IGameGraphicsStore {public string? Value;public bool Fail;public string? Read(string path)=>Value;public void Write(string path,string? value){if(Fail)throw new IOException("Mock write failure");Value=value;}}
    private sealed class NvidiaMock:INvidiaSettings {public Dictionary<uint,NvidiaValue> Values=new();public bool Fail; public NvidiaValue Read(uint id)=>Values[id];public void Write(uint id,uint value){if(Fail)throw new IOException("Injected failure");Values[id]=new(value,false);}public void Delete(uint id){Values[id]=new(0,true);}}
    private sealed class MsiMock:IGpuMsiStore{public int? Value;public bool Fail;public int Writes;public int? Read(string path)=>Value;public void Write(string path,int? value){Writes++;if(Fail)throw new IOException("Injected write failure");Value=value;}}
    private sealed class PowerMock:IPcieLinkPowerStore{public Guid Plan=Guid.NewGuid();public uint Value=2;public bool Fail;public Guid Active()=>Plan;public uint Read(Guid plan)=>Value;public void Write(Guid plan,uint value){if(Fail)throw new IOException("Injected failure");Value=value;}}
    private static EliteWorkspace? FindWorkspace(DependencyObject node)
    {
        if (node is EliteWorkspace workspace) return workspace;
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(node); index++)
        {
            var match = FindWorkspace(VisualTreeHelper.GetChild(node, index));
            if (match != null) return match;
        }
        return null;
    }
    private static NvidiaSettingsPanel? FindExtra(DependencyObject node){if(node is NvidiaSettingsPanel panel)return panel;for(int i=0;i<VisualTreeHelper.GetChildrenCount(node);i++){var found=FindExtra(VisualTreeHelper.GetChild(node,i));if(found!=null)return found;}return null;}
    private static GpuMsiPanel? FindNvidia(DependencyObject node){if(node is GpuMsiPanel panel)return panel;for(int i=0;i<VisualTreeHelper.GetChildrenCount(node);i++){var found=FindNvidia(VisualTreeHelper.GetChild(node,i));if(found!=null)return found;}return null;}
    private static GpuView? FindGpu(DependencyObject node){if(node is GpuView gpu)return gpu;for(int i=0;i<VisualTreeHelper.GetChildrenCount(node);i++){var found=FindGpu(VisualTreeHelper.GetChild(node,i));if(found!=null)return found;}return null;}
    private static ScrollViewer? FindScroll(DependencyObject node){if(node is ScrollViewer viewer && viewer.ExtentHeight>viewer.ViewportHeight)return viewer;for(int i=0;i<VisualTreeHelper.GetChildrenCount(node);i++){var found=FindScroll(VisualTreeHelper.GetChild(node,i));if(found!=null)return found;}return null;}
    private static bool HasPanel(DependencyObject node){if(node is GameGraphicsPanel)return true;for(int i=0;i<VisualTreeHelper.GetChildrenCount(node);i++)if(HasPanel(VisualTreeHelper.GetChild(node,i)))return true;return false;}
    public static async Task<bool> RunAsync(string[] args){int index=Array.IndexOf(args,"--verify-graphics");if(index<0)return false;string report=args[index+1];int checks=0;string root=Path.Combine(Path.GetTempPath(),"quantix-gpu-tests-"+Guid.NewGuid().ToString("N"));
        void Check(bool ok,string message){if(!ok)throw new Exception(message);checks++;}
        void Reject(Action action){try{action();}catch(IOException){checks++;return;}throw new Exception("Unsafe action accepted");}
        try{
            async Task RejectAsync(Func<Task> action){try{await action();}catch(IOException){checks++;return;}throw new Exception("Unsafe MSI action accepted");}
            var device=new GpuInterruptDevice(@"PCI\VEN_10DE&DEV_TEST\INSTANCE","Mock GPU",3,"driver1");var devices=new[]{device};var msiStore=new MsiMock();var msi=new GpuMsiService(msiStore,Path.Combine(root,"msi"),()=>Task.FromResult(devices),()=>true);
            await msi.Change(device,true);Check(msiStore.Value==1&&msi.HasOriginal(device),"MSI target/baseline missing");await msi.Change(device,true);await msi.Change(device,false);Check(msiStore.Value==null&&!msi.HasOriginal(device),"Missing MSI original lost");msiStore.Value=0;await msi.Change(device,true);await msi.Change(device,false);Check(msiStore.Value==0,"Original disabled MSI value lost");
            devices=new[]{device with{Support=1}};int writes=msiStore.Writes;await RejectAsync(()=>msi.Change(devices[0],true));Check(msiStore.Writes==writes,"Unsupported MSI GPU modified");devices=new[]{device};var denied=new GpuMsiService(msiStore,Path.Combine(root,"denied"),()=>Task.FromResult(devices),()=>false);await RejectAsync(()=>denied.Change(device,true));Check(msiStore.Writes==writes,"MSI rights check too late");
            msiStore.Fail=true;await RejectAsync(()=>msi.Change(device,true));Check(msi.HasOriginal(device),"MSI failure lost baseline");msiStore.Fail=false;await msi.Change(device,false);await msi.Change(device,true);devices=new[]{device with{Driver="driver2"}};await RejectAsync(()=>msi.Change(devices[0],false));Check(msi.HasOriginal(device),"Driver change lost MSI backup");devices=new[]{device};msiStore.Value=7;await RejectAsync(()=>msi.Change(device,false));msiStore.Value=1;await msi.Change(device,false);Reject(()=>GpuMsiService.PathFor(@"PCI\VEN_1002&DEV_TEST\INSTANCE"));Reject(()=>GpuMsiService.PathFor(@"PCI\VEN_10DE&DEV_TEST\.."));
            var powerStore=new PowerMock();var power=new PcieLinkPowerService(powerStore,Path.Combine(root,"power"),()=>true);power.Change(true);power.Change(true);Check(powerStore.Value==0&&power.HasOriginal,"PCIe target/baseline missing");power.Change(false);Check(powerStore.Value==2&&!power.HasOriginal,"PCIe original lost");powerStore.Fail=true;Reject(()=>power.Change(true));Check(power.HasOriginal,"PCIe failure lost backup");powerStore.Fail=false;power.Change(false);power.Change(true);Guid oldPlan=powerStore.Plan;powerStore.Plan=Guid.NewGuid();Reject(()=>power.Change(false));powerStore.Plan=oldPlan;powerStore.Value=1;Reject(()=>power.Change(false));powerStore.Value=0;power.Change(false);
            var liveDevices=await GpuMsiService.Discover();File.WriteAllText(report+".msi-readonly.txt",string.Join("\n",liveDevices.Select(d=>d+" MSISupported="+new GpuMsiService().Read(d)))+"\nPCIe AC policy="+new PcieLinkPowerService().Current());
            using(var profileStream=System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("QuantixTweakingUtility.QuantixNvidiaProfile")!){var profile=System.Xml.Linq.XDocument.Load(profileStream);var ids=profile.Descendants("SettingID").Select(x=>uint.Parse(x.Value)).ToHashSet();Check(!NvidiaSettingsService.Options.Any(option=>ids.Contains(option.Id)),"New GPU controls overlap Inspector profile IDs");Check(NvidiaSettingsService.CardOptions.GroupBy(option=>option.Id).All(group=>group.Count()==1),"Duplicate NVIDIA card controls");var maximum=NvidiaSettingsService.CardOptions.Single(option=>option.Id==0x1057EB71);var profilePower=profile.Descendants("ProfileSetting").Single(setting=>(string?)setting.Element("SettingID")==maximum.Id.ToString());Check(uint.Parse(profilePower.Element("SettingValue")!.Value)==maximum.Target,"Power card and Inspector profile disagree");}
            var mock=new NvidiaMock();var nv=new NvidiaSettingsService(mock,Path.Combine(root,"nvidia"));
            foreach(var option in NvidiaSettingsService.Options){mock.Values[option.Id]=new(0,true);nv.Change(option.Id,true);Check(mock.Read(option.Id).Value==option.Target,"NVIDIA target failed");nv.Change(option.Id,true);nv.Change(option.Id,false);Check(mock.Read(option.Id)==new NvidiaValue(0,true)&&!nv.HasOriginal(option.Id),"Inherited NVIDIA original not restored");mock.Values[option.Id]=new(123,false);nv.Change(option.Id,true);nv.Change(option.Id,false);Check(mock.Read(option.Id)==new NvidiaValue(123,false),"Custom NVIDIA original lost");}
            var first=NvidiaSettingsService.Options[0];mock.Values[first.Id]=new(0,true);nv.Change(first.Id,true);mock.Values[first.Id]=new(77,false);Reject(()=>nv.Change(first.Id,false));Check(nv.HasOriginal(first.Id),"NVIDIA conflict backup lost");mock.Values[first.Id]=new(first.Target,false);nv.Change(first.Id,false);mock.Fail=true;Reject(()=>nv.Change(first.Id,true));Check(nv.HasOriginal(first.Id),"NVIDIA write failure backup lost");mock.Fail=false;nv.Change(first.Id,false);
            var native=new NvidiaControlDriver();var live=new List<string>();foreach(var option in NvidiaSettingsService.CardOptions){try{live.Add(option.Title+": "+native.Read(option.Id));}catch(Exception ex){live.Add(option.Title+": unsupported: "+ex.Message);}}File.WriteAllLines(report+".nvidia-readonly.txt",live);
            string path=Path.Combine(root,"game.exe");var store=new Store{Value="AutoHDREnable=0;AppStatus=4096;"};var service=new GameGraphicsService(store,root,()=>new(22621,new[]{"GPU A","GPU B"}),false);
            service.Apply(path,"GpuPreference");Check(store.Value=="AutoHDREnable=0;AppStatus=4096;GpuPreference=2;","Unrelated preferences lost");service.Apply(path,"GpuPreference");service.Apply(path,"SwapEffectUpgradeEnable");
            service.Restore(path,"GpuPreference");Check(GameGraphicsService.Token(store.Value,"SwapEffectUpgradeEnable")=="1","Restored another feature");service.Restore(path,"SwapEffectUpgradeEnable");Check(store.Value=="AutoHDREnable=0;AppStatus=4096;","Round trip failed");
            store.Value=null;service.Apply(path,"GpuPreference");service.Restore(path,"GpuPreference");Check(store.Value==null,"Missing original not preserved");
            store.Value="";service.Apply(path,"GpuPreference");service.Restore(path,"GpuPreference");Check(store.Value=="","Empty original not preserved");
            store.Value=null;service.Apply(path,"GpuPreference");store.Value="GpuPreference=1;";Reject(()=>service.Restore(path,"GpuPreference"));Check(service.HasOriginal(path,"GpuPreference"),"Conflict lost backup");store.Value="GpuPreference=2;";service.Restore(path,"GpuPreference");
            store.Fail=true;Reject(()=>service.Apply(path,"GpuPreference"));Check(service.HasOriginal(path,"GpuPreference"),"Write failure lost backup");store.Fail=false;service.Restore(path,"GpuPreference");
            Reject(()=>GameGraphicsService.Patch("GpuPreference=1;GpuPreference=2;","GpuPreference","2"));Reject(()=>GameGraphicsService.Patch("bad format","GpuPreference","2"));
            var unsupported=new GameGraphicsService(store,root,()=>new(19045,new[]{"GPU"}),false);Reject(()=>unsupported.Apply(path,"GpuPreference"));Reject(()=>unsupported.Apply(path,"SwapEffectUpgradeEnable"));Reject(()=>service.Apply(path,"Unknown"));
            var tweaks=App.Services.GetRequiredService<ITweakService>();var catalog=TweakTabCatalog.Build(tweaks);Check(!catalog["gpu"].Groups.SelectMany(g=>g.Rows).Any(r=>r.Definition.RestoreOnly),"Retired GPU rows remain");Check(tweaks.GetAllTweaks().Any(t=>t.RestoreOnly&&t.Category==Models.TweakCategory.Gpu),"Recovery definitions lost");
            var main=App.Services.GetRequiredService<MainWindow>();var vm=App.Services.GetRequiredService<MainViewModel>();main.WindowStartupLocation=WindowStartupLocation.Manual;main.Left=-12000;main.Top=-12000;main.ShowInTaskbar=false;main.Width=1280;main.Height=900;main.Show();vm.SelectedNavItem=vm.NavItems.Single(n=>n.Key=="gpu");main.UpdateLayout();FindWorkspace(main)?.OpenGraphicsTools();main.UpdateLayout();await Task.Delay(100);var nvPanel=FindNvidia(main);Check(nvPanel!=null,"NVIDIA panel missing");await nvPanel!.Initialization;Check(nvPanel.RowCount>=2,"GPU hardware rows missing after initialization");Check(catalog["gpu"].Groups.SelectMany(g=>g.Rows).Count(r=>r.Definition.Id.StartsWith("gpu.nvidia.native."))==NvidiaSettingsService.CardOptions.Length,"Native NVIDIA card controls missing");Check(FindExtra(main)==null || NvidiaSettingsService.LegacyOptions.Any(o=>new NvidiaSettingsService().HasOriginal(o.Id)),"Duplicate NVIDIA controls in graphics tools");main.UpdateLayout();Check(HasPanel(main),"Actual GPU navigation panel missing");
            var bitmap=new RenderTargetBitmap(1280,900,96,96,PixelFormats.Pbgra32);bitmap.Render(main);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var file=File.Create(report+".png"))encoder.Save(file);
            var scroll=FindScroll(FindGpu(main) ?? throw new Exception("GPU view missing"));Check(scroll!=null,"GPU scrolling missing");scroll!.ScrollToVerticalOffset(700);main.UpdateLayout();var bottom=new RenderTargetBitmap(1280,900,96,96,PixelFormats.Pbgra32);bottom.Render(main);var bottomEncoder=new PngBitmapEncoder();bottomEncoder.Frames.Add(BitmapFrame.Create(bottom));using(var file=File.Create(report+".lower.png"))bottomEncoder.Save(file);File.WriteAllText(report+".txt",$"PASS: {checks} graphics preference, exact restore, conflict, capability, failure and actual UI checks. Mock preference storage only; no live graphics changes.");main.Close();Application.Current.Shutdown(0);
        }catch(Exception ex){File.WriteAllText(report+".error.txt",ex.ToString());Application.Current.Shutdown(76);}return true;
    }
}












