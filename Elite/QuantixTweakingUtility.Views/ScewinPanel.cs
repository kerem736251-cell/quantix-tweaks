using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Markup;
using System.Windows.Media;
using Microsoft.Win32;
using QuantixTweakingUtility.Services;

namespace QuantixTweakingUtility.Views;

public sealed class ScewinPanel : UserControl
{
    private readonly ScewinService service=new();
    private readonly StackPanel rows=new();
    private readonly TextBlock status=new(){TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,12,0,12)};
    private readonly TextBlock tool=new(){Text="Choose your motherboard-compatible BIOS tool package.",TextWrapping=TextWrapping.Wrap};
    private readonly ProgressBar progress=new(){IsIndeterminate=true,Height=5,Visibility=Visibility.Collapsed};
    private readonly List<Button> buttons=new();
    private static readonly ControlTemplate SwitchTemplate=(ControlTemplate)XamlReader.Parse("<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='ToggleButton'><Border x:Name='Track' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' Background='#30283E' CornerRadius='14' Width='48' Height='26'><Ellipse x:Name='Thumb' Width='18' Height='18' Fill='#A39BAD' HorizontalAlignment='Left' Margin='4,0,0,0'/></Border><ControlTemplate.Triggers><Trigger Property='IsChecked' Value='True'><Setter TargetName='Track' Property='Background' Value='#8D40EF'/><Setter TargetName='Thumb' Property='Fill' Value='White'/><Setter TargetName='Thumb' Property='HorizontalAlignment' Value='Right'/><Setter TargetName='Thumb' Property='Margin' Value='0,0,4,0'/></Trigger><Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='0.45'/></Trigger></ControlTemplate.Triggers></ControlTemplate>");
    private bool busy;
    public ScewinPanel() {
        
        Background=new SolidColorBrush(Color.FromRgb(14,11,22));Foreground=Brushes.White;
        var layout=new StackPanel {Margin=new Thickness(0,24,0,24)};Content=layout;
        var header=new StackPanel();layout.Children.Add(header);
        header.Children.Add(new TextBlock{Text="BIOS Tweaks",FontSize=25,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,12)});
        header.Children.Add(new TextBlock{Text="Experimental settings for exposed AMI firmware options. Availability does not prove motherboard compatibility or better performance. Use a package supplied for your motherboard. Keep a firmware recovery method and BitLocker recovery key available; Windows restore points cannot restore BIOS settings.",TextWrapping=TextWrapping.Wrap,Foreground=Brushes.LightSteelBlue,Margin=new Thickness(0,0,0,12)});
        header.Children.Add(new TextBlock{Text="Each switch applies one tweak immediately. OFF restores its saved original. Restart Windows after changes, then read BIOS again to verify.",TextWrapping=TextWrapping.Wrap,Foreground=Brushes.LightSteelBlue,Margin=new Thickness(0,0,0,8)});
        var commands=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(0,12,0,0)};header.Children.Add(commands);
        Add(commands,"SCAN BIOS",()=>Work(async()=>{if(!AdminHelper.IsRunningAsAdministrator())throw new InvalidOperationException("Restart Elite as administrator to initialize BIOS controls.");if(service.ToolPath.Length==0)service.ToolPath=await BiosToolSetup.PrepareAsync(message=>Dispatcher.Invoke(()=>status.Text=message));await service.LoadAsync();ShowRows(service.Snapshot);return "BIOS read complete. Available switches are ready.";}));
        header.Children.Add(progress);header.Children.Add(status);
        var footer=new StackPanel{Margin=new Thickness(0,12,0,0)};
        var backupButtons=new WrapPanel{Margin=new Thickness(0,10,0,0)};
        Add(backupButtons,"Export settings",Export);Add(backupButtons,"Import reviewed settings",Import);Add(backupButtons,"Restore original values",Restore);Add(backupButtons,"Change BIOS tool package",Choose);
        var backupContent=new StackPanel();backupContent.Children.Add(tool);backupContent.Children.Add(backupButtons);
        footer.Children.Add(new Expander{Header="Backups and recovery",Foreground=Brushes.LightSteelBlue,Content=backupContent});
        layout.Children.Add(rows);layout.Children.Add(footer);
        ShowRows("");status.Text="Click SCAN BIOS. Elite automatically prepares its BIOS support files on first use, then reads exposed settings. Run Elite as administrator. No file selection or manual import required.";
        Loaded+=(_,_)=>{var owner=Window.GetWindow(this);if(owner!=null)owner.Closing+=(_,e)=>{if(busy)e.Cancel=true;};};
    }
    private void Add(Panel parent,string label,Action action) {
        var button=new Button{Content=label,Style=Application.Current.TryFindResource("PrimaryButtonStyle") as Style,HorizontalAlignment=HorizontalAlignment.Left,Margin=new Thickness(0,0,8,8),Padding=new Thickness(16,10,16,10),MinHeight=38};
        button.Click+=(_,_)=>action();buttons.Add(button);parent.Children.Add(button);
    }
    private void Choose() {
        var dialog=new OpenFileDialog{Title="Choose compatible BIOS tool",Filter="BIOS tool|SCEWIN_64.exe"};
        if(dialog.ShowDialog(Window.GetWindow(this))==true){service.ToolPath=dialog.FileName;tool.Text=dialog.FileName;status.Text="Package selected. Read BIOS to check whether it exposes usable settings. Administrator rights required.";}
    }
    internal void ShowRows(string snapshot,string? testVendor=null) {
        rows.Children.Clear();var questions=ScewinService.Parse(snapshot);
        string vendor=testVendor??service.CpuVendor;
        var visible=ScewinService.Presets;
        if(snapshot.Length==0){rows.Children.Add(new TextBlock{Text="Scan BIOS to load the controls available on this PC.",TextWrapping=TextWrapping.Wrap,Foreground=Brushes.LightSteelBlue,Margin=new Thickness(0,0,0,16)});return;}
        var available=visible.Where(p=>ScewinService.Resolve(questions,p,vendor)!=null).OrderBy(p=>p.Category=="Gaming performance"?0:1).ToArray();
        rows.Children.Add(new TextBlock{Text=available.Length==0?"No supported controls were found in this BIOS scan. Saved backups remain available under Backups and recovery.":vendor+" • "+available.Length+" firmware controls found",TextWrapping=TextWrapping.Wrap,Foreground=Brushes.LightSteelBlue,Margin=new Thickness(0,0,0,16)});
        var targetRows=rows;
        foreach(var group in available.GroupBy(p=>p.Category)) {
        targetRows.Children.Add(new TextBlock{Text=group.Key.ToUpperInvariant(),Foreground=new SolidColorBrush(Color.FromRgb(180,117,255)),FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,12,0,12)});
        if(group.Key=="Gaming performance")targetRows.Children.Add(new TextBlock{Text="Performance features, not a guaranteed FPS increase. Already enabled or automatic settings may offer no additional gain. Compare average FPS and 1% lows in the same game scene before keeping a change.",TextWrapping=TextWrapping.Wrap,Foreground=Brushes.LightSteelBlue,Margin=new Thickness(0,0,0,12)});
        foreach(var preset in group) {
            var matched=ScewinService.ResolveMany(questions,preset,vendor);
            var question=matched.FirstOrDefault();
            var card=new StackPanel{Margin=new Thickness(14)};
            bool active=matched.Length>0 && matched.All(q=>q.Options.Single(o=>o.Selected).Label.Equals(preset.Target,StringComparison.OrdinalIgnoreCase));
            bool mixed=matched.Length>1 && matched.Select(q=>q.Options.Single(o=>o.Selected).Label).Distinct(StringComparer.OrdinalIgnoreCase).Count()>1;
            bool saved=matched.Any(q=>service.HasOriginal(q.Key));
            var line=new Grid();line.ColumnDefinitions.Add(new ColumnDefinition());line.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
            line.Children.Add(new TextBlock{Text=preset.Title+(preset.Block.Length>0?" • GUIDANCE":""),FontWeight=FontWeights.SemiBold,TextWrapping=TextWrapping.Wrap,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,20,0)});
            var switchBox=new StackPanel{HorizontalAlignment=HorizontalAlignment.Right};Grid.SetColumn(switchBox,1);
            var toggle=new ToggleButton{Tag="BiosTweak",Template=SwitchTemplate,IsChecked=active,IsEnabled=question!=null && (!active || saved),ToolTip=active&&!saved?"Already configured. No saved original to restore.":"On applies this tweak. Off restores its saved original value."};
            var state=new TextBlock{Text=question==null?(snapshot.Length==0?"NOT READ":preset.Vendor!="Any" && preset.Vendor!=vendor?"OTHER CPU":preset.Block.Length>0?"UNAVAILABLE":"NOT EXPOSED"):mixed?"MIXED":active?"ON":"OFF",FontSize=10,HorizontalAlignment=HorizontalAlignment.Center,Foreground=Brushes.LightSteelBlue,Margin=new Thickness(0,4,0,0)};
            switchBox.Children.Add(toggle);switchBox.Children.Add(state);line.Children.Add(switchBox);card.Children.Add(line);
            if(question!=null)toggle.Click+=(_,_)=>{
                bool turnOn=toggle.IsChecked==true;toggle.IsChecked=active;
                try {var keys=matched.Select(q=>q.Key).ToArray();string proposed=turnOn?ScewinService.Patch(service.Snapshot,matched.ToDictionary(q=>q.Key,q=>q.Options.Single(o=>o.Label.Equals(preset.Target,StringComparison.OrdinalIgnoreCase)).Value)):service.RestoreGroupProposal(keys);state.Text="WORKING";Submit(proposed,!turnOn,null,false,turnOn?null:keys);if(!busy)state.Text=active?"ON":"OFF";}
                catch(Exception ex){status.Text="Tweak stopped: "+ex.Message;}
            };
            bool otherCpu=vendor.Length>0 && preset.Vendor!="Any" && preset.Vendor!=vendor;
            card.Children.Add(new TextBlock{Text=otherCpu?"For "+preset.Vendor+" processors. Your detected CPU is "+vendor+".":preset.Block.Length>0?"Not implemented for automatic application: "+preset.Block:question==null?(snapshot.Length==0?"Read BIOS to check availability.":"This firmware does not expose a unique supported setting/choice."):(matched.Length>1?matched.Length+" firmware entries • Current: "+string.Join(" / ",matched.Select(q=>q.Options.Single(o=>o.Selected).Label).Distinct(StringComparer.OrdinalIgnoreCase)):question.Name+" • Current: "+question.Options.Single(o=>o.Selected).Label)+" → "+preset.Target,Foreground=Brushes.LightSteelBlue,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,5)});
            card.Children.Add(new TextBlock{Text=preset.Detail,TextWrapping=TextWrapping.Wrap,Foreground=Brushes.LightGray});
            if(active&&!saved)card.Children.Add(new TextBlock{Text="Already configured • no saved original value to restore.",Foreground=Brushes.LightSteelBlue,FontSize=11,Margin=new Thickness(0,6,0,0)});
            targetRows.Children.Add(new Border{Child=card,Background=new SolidColorBrush(Color.FromRgb(24,19,36)),CornerRadius=new CornerRadius(8),Margin=new Thickness(0,0,0,10)});
        }
        }
    }
    private async void Work(Func<Task<string>> action) {
        if(busy)return;busy=true;foreach(var button in buttons)button.IsEnabled=false;rows.IsEnabled=false;progress.Visibility=Visibility.Visible;status.Text="Working… Keep Elite open until this finishes.";
        try{status.Text=await action();}catch(Exception ex){status.Text="Operation stopped: "+ex.Message+"\nSaved exports/original values are retained.";}
        finally{busy=false;foreach(var button in buttons)button.IsEnabled=true;rows.IsEnabled=true;progress.Visibility=Visibility.Collapsed;try{ShowRows(service.Snapshot);}catch(Exception ex){status.Text+="\nCould not refresh switches: "+ex.Message;}}
    }
    private void Export() {
        if(service.Snapshot.Length==0){status.Text="Read BIOS first.";return;}
        var dialog=new SaveFileDialog{Filter="SCEWIN export|*.txt",FileName="Quantix-BIOS-export.txt"};
        if(dialog.ShowDialog(Window.GetWindow(this))==true)try{File.WriteAllBytes(dialog.FileName,Encoding.Latin1.GetBytes(service.Snapshot));status.Text="Export saved. Automatic backups: "+service.BackupFolder;}catch(Exception ex){status.Text=ex.Message;}
    }
    private void Import() {
        if(service.Snapshot.Length==0){status.Text="Read BIOS first.";return;}
        var dialog=new OpenFileDialog{Filter="Reviewed SCEWIN export|*.txt"};
        if(dialog.ShowDialog(Window.GetWindow(this))==true)try{Submit(Encoding.Latin1.GetString(File.ReadAllBytes(dialog.FileName)),false);}catch(Exception ex){status.Text="Import blocked: "+ex.Message;}
    }
    private void Restore() {try{Submit(service.RestoreProposal(),true);}catch(Exception ex){status.Text=ex.Message;}}
    private void Submit(string proposed,bool restoring,string? restoreKey=null,bool review=true,string[]? restoreKeys=null) {
        var changes=ScewinService.Review(service.Snapshot,proposed,service.CpuVendor);
        if(changes.Count==0){status.Text="No changes needed.";return;}
        string preview=string.Join("\n",changes.Select(c=>{var q=service.Questions.Single(q=>q.Key==c.Key);return q.Name+": "+q.Options.Single(o=>o.Selected).Label+" → "+q.Options.Single(o=>o.Value==c.Value).Label;}));
        if(review && MessageBox.Show(Window.GetWindow(this),preview+"\n\nOriginals will be saved before importing. BIOS changes can affect boot and stability. Confirm you have firmware recovery instructions and your BitLocker recovery key. Apply these changes?",restoring?"Restore BIOS originals":"Review BIOS changes",MessageBoxButton.YesNo,MessageBoxImage.Warning,MessageBoxResult.No)!=MessageBoxResult.Yes)return;
        Work(async()=>{string result=await service.ApplyAsync(proposed,restoring,restoreKey,restoreKeys);ShowRows(service.Snapshot);return result;});
    }
}

