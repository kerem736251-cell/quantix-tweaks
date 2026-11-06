using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using Ellipse=System.Windows.Shapes.Ellipse;
using QuantixTweakingUtility.Services;
namespace QuantixTweakingUtility.Views;
public sealed class AutomaticCleanupPanel : UserControl {
    internal static readonly string[] Targets = { "Microsoft.BingNews", "Microsoft.BingWeather", "Microsoft.Getstarted", "Microsoft.WindowsFeedbackHub", "Microsoft.Microsoft3DViewer", "Microsoft.MicrosoftOfficeHub", "Microsoft.BingFinance", "Microsoft.BingSports", "MapsBroker", "Fax", "RetailDemo", "WMPNetworkSvc", "DiagTrack", "RemoteRegistry", "Microsoft.BingTravel", "Microsoft.BingFoodAndDrink", "Microsoft.BingHealthAndFitness", "Microsoft.549981C3F5F10", "OptionalWidgets", "OptionalNewsFeeds", "OptionalTaskFeedback", "OptionalTaskFeedbackScenario", "OptionalTaskCeip", "OptionalTaskUsbCeip", "OptionalTaskMapsToast", "OptionalTaskMapsUpdate" };
    private static bool running;
    private static readonly string StatePath=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Quantix","Elite","GamingCleanup","automatic-active.flag");
    private readonly ToggleButton toggle=new(){Width=48,Height=26};
    private readonly TextBlock status=new(){TextWrapping=TextWrapping.Wrap,Foreground=Brushes.LightSteelBlue,Margin=new Thickness(0,10,0,0)};
    private readonly ProgressBar progress=new(){Height=6,Visibility=Visibility.Collapsed,Margin=new Thickness(0,12,0,0),IsIndeterminate=true};
    private readonly Border optimization=new(){Visibility=Visibility.Collapsed,CornerRadius=new CornerRadius(10),BorderThickness=new Thickness(1),BorderBrush=new SolidColorBrush(Color.FromRgb(142,75,241)),Background=new SolidColorBrush(Color.FromRgb(30,21,45)),Margin=new Thickness(0,16,0,8),Padding=new Thickness(16)};
    private readonly RotateTransform spin=new();
    private readonly DropShadowEffect glow=new(){Color=Color.FromRgb(147,78,255),BlurRadius=18,ShadowDepth=0,Opacity=0.4};
    private readonly TextBlock phase=new(){FontSize=15,FontWeight=FontWeights.SemiBold,Foreground=Brushes.White};
    private readonly TextBlock percent=new(){FontSize=24,FontWeight=FontWeights.Bold,Foreground=new SolidColorBrush(Color.FromRgb(190,144,255))};
    private readonly TextBlock item=new(){Foreground=Brushes.LightSteelBlue,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,6,0,0)};
    private readonly TextBox report=new(){IsReadOnly=true,TextWrapping=TextWrapping.Wrap,MaxHeight=180,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Background=Brushes.Transparent,Foreground=Brushes.LightSteelBlue,BorderThickness=new Thickness(0)};
    private readonly Expander details=new(){Header="Optimization report",Foreground=Brushes.LightSteelBlue,Visibility=Visibility.Collapsed,Margin=new Thickness(0,10,0,0)};
    internal bool OptimizationVisible=>optimization.Visibility==Visibility.Visible;
    internal bool AnimationRunning=>spin.HasAnimatedProperties || glow.HasAnimatedProperties;
    internal double ShownProgress=>progress.Value;
    internal void PreviewOptimization(bool visible){SetBusyVisuals(visible,true);if(visible)UpdateProgress(4,20,"Microsoft.BingNews",true);}
    private bool updating;
    public bool IsWorking=>running;
    public AutomaticCleanupPanel(){
        var body=new StackPanel{Margin=new Thickness(18)};
        var header=new DockPanel();toggle.HorizontalAlignment=HorizontalAlignment.Right;DockPanel.SetDock(toggle,Dock.Right);header.Children.Add(toggle);
        header.Children.Add(new TextBlock{Text="Uninstall unneeded services and apps",FontSize=18,FontWeight=FontWeights.SemiBold,Foreground=Brushes.White});body.Children.Add(header);
        body.Children.Add(new TextBlock{Text="ON removes optional apps for the Windows account running Elite: news, weather, tips, legacy Cortana and hub apps; disables optional services and Widgets/news feeds on supported Windows editions, plus optional feedback and offline-map scheduled tasks. OFF restores saved services, tasks and feed settings. Removed apps need reinstalling from Microsoft Store. Search, browsers, Discord, Steam, games, Xbox, audio, Bluetooth, printing, networking, updates, security and drivers are protected. Managed PCs keep their service/policy configuration.",TextWrapping=TextWrapping.Wrap,Foreground=Brushes.LightSteelBlue,Margin=new Thickness(0,10,0,0)});
        var workLayout=new Grid();workLayout.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(68)});workLayout.ColumnDefinitions.Add(new ColumnDefinition());
        var ring=new Ellipse{Width=46,Height=46,StrokeThickness=4,Stroke=new LinearGradientBrush(Color.FromArgb(20,147,78,255),Color.FromRgb(190,144,255),45),StrokeDashArray=new DoubleCollection{18,6},RenderTransform=spin,RenderTransformOrigin=new Point(0.5,0.5),VerticalAlignment=VerticalAlignment.Center,HorizontalAlignment=HorizontalAlignment.Left};workLayout.Children.Add(ring);
        var workText=new StackPanel();Grid.SetColumn(workText,1);workLayout.Children.Add(workText);var headline=new DockPanel();DockPanel.SetDock(percent,Dock.Right);headline.Children.Add(percent);headline.Children.Add(phase);workText.Children.Add(headline);workText.Children.Add(item);optimization.Child=workLayout;optimization.Effect=glow;
        body.Children.Add(optimization);progress.Foreground=new SolidColorBrush(Color.FromRgb(160,87,255));body.Children.Add(progress);body.Children.Add(status);details.Content=report;body.Children.Add(details);
        Content=new Border{Child=body,CornerRadius=new CornerRadius(12),Background=new SolidColorBrush(Color.FromRgb(20,16,30)),Margin=new Thickness(30,20,30,12)};
        toggle.Template=(ControlTemplate)XamlReader.Parse("<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='ToggleButton'><Border x:Name='Track' Background='#30283E' CornerRadius='14'><Ellipse x:Name='Thumb' Width='18' Height='18' Fill='#A39BAD' HorizontalAlignment='Left' Margin='4,0,0,0'/></Border><ControlTemplate.Triggers><Trigger Property='IsChecked' Value='True'><Setter TargetName='Track' Property='Background' Value='#8D40EF'/><Setter TargetName='Thumb' Property='HorizontalAlignment' Value='Right'/><Setter TargetName='Thumb' Property='Fill' Value='White'/><Setter TargetName='Thumb' Property='Margin' Value='0,0,4,0'/></Trigger></ControlTemplate.Triggers></ControlTemplate>");
        Refresh();toggle.Click+=async(_,_)=>await ExecuteAsync(toggle.IsChecked==true);
        Loaded+=(_,_)=>{Refresh();var owner=Window.GetWindow(this);if(owner!=null)owner.Closing+=(_,e)=>{if(running)e.Cancel=true;};};
    }
    private void SetBusyVisuals(bool value,bool apply){
        optimization.Visibility=value?Visibility.Visible:Visibility.Collapsed;progress.Visibility=value?Visibility.Visible:Visibility.Collapsed;
        spin.BeginAnimation(RotateTransform.AngleProperty,null);glow.BeginAnimation(DropShadowEffect.OpacityProperty,null);spin.Angle=0;
        if(value){phase.Text=apply?"OPTIMIZING BACKGROUND ACTIVITY":"RESTORING ORIGINAL SETTINGS";percent.Text="…";item.Text="Preparing your cleanup workflow";progress.IsIndeterminate=true;progress.Value=0;details.Visibility=Visibility.Collapsed;
            if(SystemParameters.ClientAreaAnimation){spin.BeginAnimation(RotateTransform.AngleProperty,new DoubleAnimation(0,360,TimeSpan.FromSeconds(1.3)){RepeatBehavior=RepeatBehavior.Forever});glow.BeginAnimation(DropShadowEffect.OpacityProperty,new DoubleAnimation(0.2,0.65,TimeSpan.FromSeconds(1)){AutoReverse=true,RepeatBehavior=RepeatBehavior.Forever});}
        }
    }
    private void UpdateProgress(int step,int total,string id,bool apply){
        if(total<=0 || step<0 || step>=total)return;
        progress.IsIndeterminate=false;progress.Value=100.0*step/total;percent.Text=((int)progress.Value)+"%";
        phase.Text=!apply?"RESTORING ORIGINAL SETTINGS":id.StartsWith("OptionalTask",StringComparison.Ordinal)?"REDUCING OPTIONAL SCHEDULED TASKS":id.StartsWith("Optional",StringComparison.Ordinal)?"APPLYING OPTIONAL WINDOWS SETTINGS":id.StartsWith("Microsoft.",StringComparison.Ordinal)?"REMOVING OPTIONAL APPS":"REDUCING OPTIONAL SERVICES";
        item.Text=$"{step} of {total} processed • {id}";
    }
    private static int CountProcesses(){var processes=System.Diagnostics.Process.GetProcesses();try{return processes.Length;}finally{foreach(var process in processes)process.Dispose();}}
    private void Refresh(){updating=true;toggle.IsChecked=File.Exists(StatePath);toggle.IsEnabled=!running;status.Text=toggle.IsChecked==true?"ON • Cleanup has been applied or needs recovery. OFF restores saved services, tasks and feed settings.":"OFF • Turn on once to apply automatically. Process counts depend on your installed apps and hardware.";updating=false;}
    private async Task ExecuteAsync(bool apply){
        if(updating||running)return;
        if(!AdminHelper.IsRunningAsAdministrator()){Refresh();status.Text="Run Elite as administrator to apply or restore cleanup. Nothing changed.";return;}
        int before=CountProcesses();
        running=true;toggle.IsEnabled=false;SetBusyVisuals(true,apply);status.Text=apply?"Applying cleanup… Keep Elite open.":"Restoring saved services, tasks and feed settings…";
        try{
            if(apply){Directory.CreateDirectory(Path.GetDirectoryName(StatePath)!);File.WriteAllText(StatePath,"Recovery available; service and policy backups are stored beside this file. Preset 3.10.21.");}
            var result=await GamingCleanupWindow.RunAsync(apply?"Apply":"Restore",apply?Targets:Array.Empty<string>(),new Progress<string>(message=>{var parts=message.Split('|');if(parts.Length==3&&int.TryParse(parts[0],out int step)&&int.TryParse(parts[1],out int total)&&total>0){UpdateProgress(step,total,parts[2],apply);}}));
            bool failed=result.Any(r=>r.State.StartsWith("Failed:",StringComparison.Ordinal));
            if(!apply&&!failed)File.Delete(StatePath);
            report.Text=string.Join("\n",result.Select(r=>r.Id+": "+r.State));details.Visibility=Visibility.Visible;
            status.Text=(failed?"Some items could not complete. Saved service settings are retained.":apply?"Cleanup applied. Restart Windows to compare background process counts.":"Saved services, tasks and feed settings restored. Removed apps can be reinstalled from Microsoft Store.")+"\nObserved processes: "+before+" → "+CountProcesses()+" (snapshot; compare again after restart).";
        }catch(Exception ex){status.Text="Cleanup stopped: "+ex.Message+" Saved service settings are retained.";}
        finally{running=false;updating=true;toggle.IsChecked=File.Exists(StatePath);updating=false;toggle.IsEnabled=true;SetBusyVisuals(false,apply);}
    }
}
