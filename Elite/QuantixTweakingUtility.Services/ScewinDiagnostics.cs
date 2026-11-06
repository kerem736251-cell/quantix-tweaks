using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Extensions.DependencyInjection;
using QuantixTweakingUtility.Views;
using QuantixTweakingUtility.ViewModels;

namespace QuantixTweakingUtility.Services;
public static class ScewinDiagnostics {
    private static ScewinPanel? FindPanel(DependencyObject root) {
        if(root is ScewinPanel panel)return panel;
        for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)if(FindPanel(VisualTreeHelper.GetChild(root,i)) is ScewinPanel found)return found;
        return null;
    }
    private static bool HasOldLaunch(DependencyObject root) {
        if(root is Button b && b.Content?.ToString()=="BIOS TWEAKS / SCEWIN")return true;
        for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)if(HasOldLaunch(VisualTreeHelper.GetChild(root,i)))return true;
        return false;
    }
    private static int CountControls(DependencyObject root,Func<DependencyObject,bool> match) {
        int count=match(root)?1:0;
        for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)count+=CountControls(VisualTreeHelper.GetChild(root,i),match);
        return count;
    }
    private static bool HasEntry(DependencyObject root) {
        if(root is ScewinPanel)return true;
        for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)if(HasEntry(VisualTreeHelper.GetChild(root,i)))return true;
        return false;
    }
    private static void Render(Window w,string path) {
        w.UpdateLayout();var bitmap=new RenderTargetBitmap((int)w.ActualWidth,(int)w.ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(w);
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var file=File.Create(path);encoder.Save(file);
    }
    public static async Task<bool> RunAsync(string[] args) {
        int index=Array.IndexOf(args,"--verify-scewin");if(index<0)return false;
        string path=args[index+1];int count=0;
        void Check(bool value,string message){if(!value)throw new Exception(message);count++;}
        void Reject(Action action,string message){try{action();}catch(IOException){count++;return;}catch(InvalidOperationException){count++;return;}throw new Exception(message);}
        try {
            string duplicateWarning="WARNING: Duplicate questions found. Only one question will be enabled in the script.\r\nScript file exported successfully.\r\n";
            Check(ScewinService.CleanOperation(0,duplicateWarning,true),"Successful duplicate-warning export rejected");
            Check(!ScewinService.CleanOperation(0,duplicateWarning,false),"Import warning ignored");
            Check(!ScewinService.CleanOperation(1,duplicateWarning,true),"Nonzero export exit ignored");
            Check(!ScewinService.CleanOperation(0,duplicateWarning+"ERROR: failure",true),"Export error ignored");
            Check(!ScewinService.CleanOperation(0,"WARNING: Different warning\r\nScript file exported successfully.",true),"Unknown warning ignored");
            string flat="HIICrc32= 12345678\r\nSetup Question = DF Cstates\r\nToken = 3C\r\nOffset = 3C\r\nWidth = 01\r\nOptions =*[00]Disabled\t// Move star\r\n [01]Enabled\r\n [FF]Auto\r\n";
            var flatQuestions=ScewinService.Parse(flat);Check(flatQuestions.Length==1,"Flat export not parsed");Check(flatQuestions[0].Options[0].Label=="Disabled","Option comment included in label");
            Check(ScewinService.Resolve(flatQuestions,ScewinService.Presets.Single(p=>p.Title=="Fabric C-states"),"AMD")!=null,"Flat fabric option unavailable");
            Check(ScewinService.Patch(ScewinService.Patch(flat,new Dictionary<string,string>{{flatQuestions[0].Key,"01"}}),new Dictionary<string,string>{{flatQuestions[0].Key,"00"}})==flat,"Flat exact round trip failed");
            string liveFolder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Quantix","Elite","BIOS");
            if(Directory.Exists(liveFolder)) {
                var latest=Directory.GetFiles(liveFolder,"export-*.txt").OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
                if(latest!=null){var savedText=System.Text.Encoding.Latin1.GetString(File.ReadAllBytes(latest));var parsed=ScewinService.Parse(savedText);foreach(var scoped in ScewinService.Presets.Where(p=>p.Group||p.Category=="Standby"||p.Category=="Gaming performance")){var matches=ScewinService.ResolveMany(parsed,scoped,"AMD");if(matches.Length==0)continue;var originals=matches.ToDictionary(q=>q.Key,q=>q.Options.Single(o=>o.Selected).Value);var targets=matches.ToDictionary(q=>q.Key,q=>q.Options.Single(o=>o.Label==scoped.Target).Value);var proposal=ScewinService.Patch(savedText,targets);Check(ScewinService.Review(savedText,proposal,"AMD").Count==matches.Count(q=>originals[q.Key]!=targets[q.Key]),"Grouped control changed another setting");Check(ScewinService.Patch(proposal,originals)==savedText,"Saved-export grouped restore round trip failed");}Check(parsed.Length>0,"Existing firmware export cannot parse");File.WriteAllText(path+".export-check.txt","Read-only saved export: "+parsed.Length+" usable questions; AMD exposed experiments: "+string.Join(", ",ScewinService.Presets.Where(p=>ScewinService.Resolve(parsed,p,"AMD")!=null).Select(p=>p.Title)));}
            }
            string fixture="HIICrc32= FE47DE9F\r\n// GUID = 7b59104a-c00d-4158-87ff-f04d6396a915\r\nSetup Question = Energy Efficient Turbo\r\nToken = 01\r\nOffset = 02\r\nWidth = 01\r\nOptions =*[01]Enabled\r\n\t[00]Disabled\r\n\r\nSetup Question = Secure Boot\r\nToken = 03\r\nOffset = 04\r\nWidth = 01\r\nOptions =*[01]Enabled\r\n\t[00]Disabled\r\n";
            var q=ScewinService.Parse(fixture);Check(q.Length==2,"Parse count");Check(q[0].Options.Single(o=>o.Selected).Value=="01","Selected flag");
            var preset=ScewinService.Presets.Single(p=>p.Title=="Energy Efficient Turbo experiment");Check(ScewinService.Resolve(q,preset,"Intel")?.Key==q[0].Key,"Exposed preset");
            string changed=ScewinService.Patch(fixture,new Dictionary<string,string>{{q[0].Key,"00"}});
            Check(ScewinService.Review(fixture,changed,"Intel").Count==1,"Review delta");Check(changed.Contains("Options =[01]Enabled\r\n\t*[00]Disabled"),"Format preservation");
            Check(ScewinService.Patch(changed,new Dictionary<string,string>{{q[0].Key,"01"}})==fixture,"Exact round trip");
            Check(ScewinService.Resolve(q,ScewinService.Presets[0])==null,"Missing preset");
            Check(ScewinService.Parse(fixture.Replace("*[01]","[01]")).Length==0,"Missing selection");
            Check(ScewinService.Resolve(q.Concat(q.Take(1)).ToArray(),preset,"Intel")==null,"Duplicate match");
            Reject(()=>ScewinService.Review(fixture,changed.Replace("FE47DE9F","00000000")),"Metadata edit accepted");
            Reject(()=>ScewinService.Review(fixture,ScewinService.Patch(fixture,new Dictionary<string,string>{{q[1].Key,"00"}})),"Security setting accepted");
            Reject(()=>ScewinService.Patch(fixture,new Dictionary<string,string>{{q[0].Key,"03"}}),"Invalid value accepted");
            Reject(()=>ScewinService.Review(fixture,changed+"// Extra edit"),"Extra edit accepted");
            var journal=new BiosJournal("fixture",new(){new(q[0].Key,q[0].Name,"01","00")});
            Check(ScewinService.RestoreSnapshot(changed,journal)==fixture,"Restore saved original");
            Check(ScewinService.RestoreSnapshot(fixture,journal)==fixture,"Already restored original");
            Reject(()=>ScewinService.RestoreSnapshot(changed.Replace("Energy Efficient Turbo","Other setting").Replace("Token = 01","Token = 09"),journal),"Missing restore setting accepted");
            string three=changed.Replace("\t*[00]Disabled","\t[00]Disabled\r\n\t*[02]Auto");
            Reject(()=>ScewinService.RestoreSnapshot(three,journal),"Outside change overwritten");
            var failedAttempt=new BiosJournal("fixture",new(){new(q[0].Key,q[0].Name,"01","00","02")});
            Check(ScewinService.Parse(ScewinService.RestoreSnapshot(three,failedAttempt))[0].Options.Single(o=>o.Selected).Value=="01","Pre-import value cannot restore after failed retry");
            Check(ScewinService.RestoreSnapshot(changed,journal,q[0].Key)==fixture,"Single-switch restore failed");
            Reject(()=>ScewinService.RestoreSnapshot(changed,journal,q[1].Key),"Unsaved switch restore invented a value");
            var multi=new BiosJournal("fixture",new(){new(q[0].Key,q[0].Name,"01","00"),new(q[1].Key,q[1].Name,"01","00")});
            string both=ScewinService.Patch(fixture,new Dictionary<string,string>{{q[0].Key,"00"},{q[1].Key,"00"}});
            string single=ScewinService.RestoreSnapshot(both,multi,q[0].Key);
            Check(ScewinService.Parse(single)[1].Options.Single(o=>o.Selected).Value=="00","Single restore changed another tweak");
            foreach(var item in ScewinService.Presets) {
                string vendor=item.Vendor=="Any"?"Intel":item.Vendor;
                string sample="HIICrc32= FE47DE9F\r\n// GUID = 7b59104a-c00d-4158-87ff-f04d6396a915\r\nSetup Question = "+item.Names[0]+"\r\nToken = 08\r\nOffset = 09\r\nWidth = 01\r\nOptions =*[01]"+(item.Target=="Enabled"?"Disabled":"Enabled")+"\r\n\t[00]"+item.Target+"\r\n";
                var parsed=ScewinService.Parse(sample);Check(parsed.Length==1,"Catalog fixture parse: "+item.Title);
                string patch=ScewinService.Patch(sample,new Dictionary<string,string>{{parsed[0].Key,"00"}});
                if(item.Block.Length>0 || item.Title=="Resizable BAR")Reject(()=>ScewinService.Review(sample,patch,vendor),"Blocked item accepted: "+item.Title);
                else {
                    Check(ScewinService.Resolve(parsed,item,vendor)!=null,"Catalog item unavailable: "+item.Title);
                    Check(ScewinService.Review(sample,patch,vendor).Count==1,"Catalog apply rejected: "+item.Title);
                    if(item.Vendor!="Any")Reject(()=>ScewinService.Review(sample,patch,vendor=="AMD"?"Intel":"AMD"),"Wrong CPU vendor accepted: "+item.Title);
                }
            }
            Check(ScewinService.Presets.Length==41,"Catalog count changed unexpectedly");
            string grouped="HIICrc32= 12345678\r\nSetup Question = Power Down Enable\r\nToken = 01\r\nOffset = 01\r\nWidth = 01\r\nOptions =*[01]Enabled\r\n [00]Disabled\r\n [FF]Auto\r\nSetup Question = Power Down Enable\r\nToken = 02\r\nOffset = 02\r\nWidth = 01\r\nOptions =*[FF]Auto\r\n [00]Disabled\r\n [01]Enabled\r\nSetup Question = Secure Boot\r\nToken = 03\r\nOffset = 03\r\nWidth = 01\r\nOptions =*[01]Enabled\r\n [00]Disabled\r\n";
            var groupedPreset=ScewinService.Presets.Single(p=>p.Title=="Disable memory power-down");var groupedQuestions=ScewinService.Parse(grouped);var groupedMatches=ScewinService.ResolveMany(groupedQuestions,groupedPreset,"AMD");Check(groupedMatches.Length==2,"Grouped firmware copies not resolved");
            var groupChoices=groupedMatches.ToDictionary(q=>q.Key,q=>q.Options.Single(o=>o.Label==groupedPreset.Target).Value);string groupApplied=ScewinService.Patch(grouped,groupChoices);Check(ScewinService.Review(grouped,groupApplied,"AMD").Count==2,"Grouped apply did not isolate both copies");
            var groupJournal=new BiosJournal("fixture",groupedMatches.Select(q=>new BiosOriginal(q.Key,q.Name,q.Options.Single(o=>o.Selected).Value,groupChoices[q.Key])).ToList());Check(ScewinService.RestoreSnapshot(groupApplied,groupJournal)==grouped,"Grouped distinct originals did not restore exactly");
            Check(ScewinService.ResolveMany(groupedQuestions.Concat(groupedQuestions.Take(1)).ToArray(),groupedPreset,"AMD").Length==0,"Grouped duplicate key accepted");Check(ScewinService.Parse(groupApplied).Last().Options.Single(o=>o.Selected).Label=="Enabled","Group changed unrelated security setting");
            string bar="HIICrc32= 12345678\r\n";
            string Row(string name,string token,string selected,string other)=>"Setup Question = "+name+"\r\nToken = "+token+"\r\nOffset = "+token+"\r\nWidth = 01\r\nOptions =*[00]"+selected+"\r\n [01]"+other+"\r\n";
            bar+=Row("Above 4G Decoding","01","Enabled","Disabled")+Row("CSM Support","02","Disabled","Enabled")+Row("Re-Size BAR Support","03","Disabled","Enabled");
            var barRows=ScewinService.Parse(bar);Check(ScewinService.BarPrerequisites(barRows),"ReBAR prerequisites rejected");
            string barChanged=ScewinService.Patch(bar,new Dictionary<string,string>{{barRows[2].Key,"01"}});Check(ScewinService.Review(bar,barChanged,"AMD").Count==1,"Guarded ReBAR rejected");
            Check(!ScewinService.BarPrerequisites(ScewinService.Parse(ScewinService.Patch(bar,new Dictionary<string,string>{{barRows[1].Key,"01"}}))),"Legacy boot accepted for ReBAR");
            Reject(()=>ScewinService.Review(fixture,changed,"AMD"),"Intel power option accepted on AMD");
            var main=App.Services.GetRequiredService<MainWindow>();var vm=App.Services.GetRequiredService<MainViewModel>();
            main.WindowStartupLocation=WindowStartupLocation.Manual;main.Left=-12000;main.Top=-12000;main.ShowInTaskbar=false;main.Width=1280;main.Height=900;main.Show();vm.SelectedNavItem=vm.NavItems.Single(n=>n.Key=="bios");main.UpdateLayout();
            if(vm.CurrentViewModel is BiosViewModel bios){if(bios.ScanCommand.IsRunning && bios.ScanCommand.ExecutionTask!=null)await bios.ScanCommand.ExecutionTask;else await bios.ScanCommand.ExecuteAsync(null);}
            Check(HasEntry(main),"Actual BIOS sidebar entry missing");Render(main,path+".navigation.png");
            var panel=FindPanel(main)??throw new Exception("Inline BIOS panel missing");
            Check(!HasOldLaunch(main),"Old BIOS launch button remains");
            panel.ShowRows(fixture.Replace("Energy Efficient Turbo","DF Cstates"),"AMD");main.UpdateLayout();panel.BringIntoView(new Rect(0,0,panel.ActualWidth,500));main.UpdateLayout();Render(main,path+".inline.png");
            Check(CountControls(panel,node=>node is CheckBox)==0,"Checkbox selection UI remains");
            Check(CountControls(panel,node=>node is ToggleButton && node is not CheckBox && node is not RadioButton)>=1,"Individual BIOS switches missing");
            Check(CountControls(panel,node=>node is Button button && button.Content?.ToString()=="APPLY SELECTED TWEAKS")==0,"Batch Apply button remains");
            Check(CountControls(panel,node=>node is TextBlock text && text.Text=="BIOS Tweaks")==1,"Clean BIOS heading missing");
            Check(CountControls(panel,node=>node is ToggleButton toggle && Equals(toggle.Tag,"BiosTweak"))==1,"Unsupported switches rendered after scan");Check(CountControls(panel,node=>node is TextBlock text && (text.Text=="NOT EXPOSED"||text.Text=="UNAVAILABLE"||text.Text=="OTHER CPU"||text.Text.Contains("GUIDANCE")))==0,"Unavailable placeholders remain");panel.BringIntoView(new Rect(0,Math.Max(0,panel.ActualHeight-200),panel.ActualWidth,200));main.UpdateLayout();Render(main,path+".apply.png");
            panel.ShowRows(fixture,"Intel");
            var window=new Window{Content=new ScrollViewer{Content=new ScewinPanel(),VerticalScrollBarVisibility=ScrollBarVisibility.Auto},Width=1000,Height=850,WindowStartupLocation=WindowStartupLocation.Manual,Left=-12000,Top=-12000,ShowInTaskbar=false};window.Show();Check(CountControls(window,node=>node is ToggleButton toggle && Equals(toggle.Tag,"BiosTweak"))==0,"Unscanned BIOS shows fake switches");Render(window,path+".empty.png");((ScewinPanel)((ScrollViewer)window.Content).Content).ShowRows(fixture,"Intel");Render(window,path+".fixture.png");
            if(Directory.Exists(liveFolder)){var latestPreview=Directory.GetFiles(liveFolder,"export-*.txt").OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();if(latestPreview!=null){panel.ShowRows(System.Text.Encoding.Latin1.GetString(File.ReadAllBytes(latestPreview)),"AMD");panel.BringIntoView(new Rect(0,0,panel.ActualWidth,500));main.UpdateLayout();Render(main,path+".saved-preview.png");}}
            File.WriteAllText(path+".txt",$"PASS: {count} parser/policy/navigation checks. BIOS UI rendered with synthetic fixture. No SCEWIN execution or firmware writes.");window.Close();main.Close();Application.Current.Shutdown(0);
        }catch(Exception ex){File.WriteAllText(path+".error.txt",ex.ToString());Application.Current.Shutdown(75);}
        return true;
    }
}



