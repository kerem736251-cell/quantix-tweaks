using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Markup;
using System.Windows.Media;
using QuantixTweakingUtility.Services;
namespace QuantixTweakingUtility.Views;
public sealed class NvidiaSettingsPanel:UserControl {
 private readonly bool recovery;
 public int RowCount=>rows.Children.Count;
 public Task Initialization { get; private set; } = Task.CompletedTask;
 private readonly StackPanel rows=new();private readonly TextBlock status=new(){Foreground=Brushes.LightSteelBlue,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,10,0,10)};private NvidiaSettingsService? service;private bool busy;
 private static readonly ControlTemplate Switch=(ControlTemplate)XamlReader.Parse("<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='ToggleButton'><Border x:Name='Track' Background='#30283E' CornerRadius='14' Width='48' Height='26'><Ellipse x:Name='Thumb' Width='18' Height='18' Fill='#A39BAD' HorizontalAlignment='Left' Margin='4,0,0,0'/></Border><ControlTemplate.Triggers><Trigger Property='IsChecked' Value='True'><Setter TargetName='Track' Property='Background' Value='#8D40EF'/><Setter TargetName='Thumb' Property='HorizontalAlignment' Value='Right'/><Setter TargetName='Thumb' Property='Margin' Value='0,0,4,0'/></Trigger></ControlTemplate.Triggers></ControlTemplate>");
 public NvidiaSettingsPanel(bool recovery=true){this.recovery=recovery;var layout=new StackPanel{Margin=new Thickness(0,20,0,20)};Content=layout;layout.Children.Add(new TextBlock{Text=recovery?"NVIDIA PROFILE RECOVERY":"NVIDIA EXTRA CONTROLS • 5",Foreground=new SolidColorBrush(Color.FromRgb(180,117,255)),FontWeight=FontWeights.SemiBold});layout.Children.Add(new TextBlock{Text=recovery?"Saved settings from earlier Elite versions. Turn OFF to restore their originals.":"These controls are absent from the Quantix Inspector profile. Global driver controls: ON applies the described choice, OFF restores the saved original. Restart games. These are workload-dependent options, not a guaranteed FPS preset.",Foreground=Brushes.LightSteelBlue,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,10,0,10)});layout.Children.Add(status);layout.Children.Add(rows);service=new();Initialization=Refresh();}
 private async Task Refresh(){rows.Children.Clear();if(service==null)return;foreach(var option in recovery?NvidiaSettingsService.LegacyOptions:NvidiaSettingsService.Options){if(recovery&&(!service.HasOriginal(option.Id)||System.Linq.Enumerable.Any(NvidiaSettingsService.CardOptions,card=>card.Id==option.Id)))continue;NvidiaValue? value=null;string error="";bool saved=false;try{value=await Task.Run(()=>service.Read(option.Id));saved=service.HasOriginal(option.Id);}catch(Exception ex){error=ex.Message;}
 bool active=recovery?saved:value?.Value==option.Target;var card=new DockPanel{Margin=new Thickness(14)};var right=new StackPanel();DockPanel.SetDock(right,Dock.Right);card.Children.Add(right);var toggle=new ToggleButton{Template=Switch,IsChecked=active,IsEnabled=value!=null&&(recovery?saved:(!active||saved))};right.Children.Add(toggle);var label=new TextBlock{Text=value==null?"UNSUPPORTED":active?"ON":"OFF",Foreground=Brushes.LightSteelBlue,FontSize=10,HorizontalAlignment=HorizontalAlignment.Center};right.Children.Add(label);var text=new StackPanel{Margin=new Thickness(0,0,20,0)};text.Children.Add(new TextBlock{Text=option.Title,Foreground=Brushes.White,FontWeight=FontWeights.SemiBold});text.Children.Add(new TextBlock{Text=option.Detail+(error.Length>0?"\n"+error:active&&!saved?"\nAlready configured; no original saved by Elite.":""),Foreground=Brushes.LightSteelBlue,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,0)});card.Children.Add(text);rows.Children.Add(new Border{Child=card,Background=new SolidColorBrush(Color.FromRgb(24,19,36)),CornerRadius=new CornerRadius(8),Margin=new Thickness(0,0,0,10)});
 toggle.Click+=async(_,_)=>{if(busy)return;busy=true;rows.IsEnabled=false;bool on=toggle.IsChecked==true;toggle.IsChecked=active;label.Text="WORKING";string result;try{await Task.Run(()=>service.Change(option.Id,recovery?false:on));result="Setting verified. Restart your game.";}catch(Exception ex){result="Stopped: "+ex.Message;}await Refresh();status.Text=result;rows.IsEnabled=true;busy=false;};}
 }
}






