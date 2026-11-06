using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Markup;
using System.Windows.Media;
using Microsoft.Win32;
using QuantixTweakingUtility.Services;
namespace QuantixTweakingUtility.Views;
public sealed class GameGraphicsPanel:UserControl {
    private readonly GameGraphicsService service=new();
    private readonly StackPanel rows=new();
    private readonly TextBlock status=new(){TextWrapping=TextWrapping.Wrap,Foreground=Brushes.LightSteelBlue,Margin=new Thickness(0,10,0,10)};
    private string path="";private bool busy;
    private static readonly ControlTemplate Switch=(ControlTemplate)XamlReader.Parse("<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='ToggleButton'><Border x:Name='Track' Background='#30283E' CornerRadius='14' Width='48' Height='26'><Ellipse x:Name='Thumb' Width='18' Height='18' Fill='#A39BAD' HorizontalAlignment='Left' Margin='4,0,0,0'/></Border><ControlTemplate.Triggers><Trigger Property='IsChecked' Value='True'><Setter TargetName='Track' Property='Background' Value='#8D40EF'/><Setter TargetName='Thumb' Property='HorizontalAlignment' Value='Right'/><Setter TargetName='Thumb' Property='Margin' Value='0,0,4,0'/></Trigger></ControlTemplate.Triggers></ControlTemplate>");
    public GameGraphicsPanel(){
        var body=new StackPanel{Margin=new Thickness(30,20,30,20)};Content=body;
        body.Children.Add(new TextBlock{Text="PER-GAME GRAPHICS",Foreground=new SolidColorBrush(Color.FromRgb(180,117,255)),FontWeight=FontWeights.SemiBold});
        body.Children.Add(new TextBlock{Text="Choose the game's EXE. ON applies only to that game; OFF restores its original setting. Restart the game after changes.",Foreground=Brushes.LightSteelBlue,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,10,0,10)});
        var choose=new Button{Content="CHOOSE GAME",Style=Application.Current.TryFindResource("PrimaryButtonStyle") as Style,HorizontalAlignment=HorizontalAlignment.Left,Padding=new Thickness(16,10,16,10)};
        choose.Click+=(_,_)=>{if(busy)return;var dialog=new OpenFileDialog{Filter="Game executable|*.exe",Title="Choose the actual game executable"};if(dialog.ShowDialog(Window.GetWindow(this))==true){path=dialog.FileName;Refresh();}};
        body.Children.Add(choose);body.Children.Add(status);body.Children.Add(rows);Refresh();
    }
    private void Refresh(){
        rows.Children.Clear();try{
            var caps=GameGraphicsService.Detect();status.Text=(path.Length==0?"No game selected.":path)+"\nDetected: "+string.Join(", ",caps.Adapters);
            Add("High-performance GPU preference","GpuPreference",caps.HighPerformance,"Requests the faster GPU on systems with multiple active adapters. May increase laptop power use; Windows and the game determine the actual adapter.");
            Add("Optimizations for windowed games","SwapEffectUpgradeEnable",caps.Windowed,"Windows 11 22H2+: improves presentation for supported windowed/borderless DirectX 10/11 games. Compare frame pacing; restore if worse.");
        }catch(Exception ex){status.Text="Graphics controls unavailable: "+ex.Message;}
    }
    private void Add(string title,string feature,bool compatible,string detail){
        bool active=path.Length>0 && service.Current(path,feature)==GameGraphicsService.Target(feature);
        bool saved=path.Length>0 && service.HasOriginal(path,feature);
        var card=new DockPanel{Margin=new Thickness(14)};var right=new StackPanel();DockPanel.SetDock(right,Dock.Right);card.Children.Add(right);
        var toggle=new ToggleButton{Template=Switch,IsChecked=active,IsEnabled=path.Length>0&&compatible&&(!active||saved),ToolTip=active&&!saved?"Already enabled. No original saved by Elite.":"ON applies; OFF restores the saved original."};right.Children.Add(toggle);
        var state=new TextBlock{Text=!compatible?"N/A":active?"ON":"OFF",Foreground=Brushes.LightSteelBlue,HorizontalAlignment=HorizontalAlignment.Center,FontSize=10};right.Children.Add(state);
        var text=new StackPanel{Margin=new Thickness(0,0,20,0)};text.Children.Add(new TextBlock{Text=title,Foreground=Brushes.White,FontWeight=FontWeights.SemiBold});text.Children.Add(new TextBlock{Text=detail,Foreground=Brushes.LightSteelBlue,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,0)});card.Children.Add(text);
        rows.Children.Add(new Border{Child=card,Background=new SolidColorBrush(Color.FromRgb(24,19,36)),CornerRadius=new CornerRadius(8),Margin=new Thickness(0,0,0,10)});
        toggle.Click+=async(_,_)=>{if(busy)return;bool on=toggle.IsChecked==true;toggle.IsChecked=active;busy=true;rows.IsEnabled=false;state.Text="WORKING";string selected=path;string message;
            try{await Task.Run(()=>{if(on)service.Apply(selected,feature);else service.Restore(selected,feature);});message="Setting verified. Restart the game to use it.";}catch(Exception ex){message="Stopped: "+ex.Message;}
            finally{busy=false;rows.IsEnabled=true;}Refresh();status.Text+="\n"+message;
        };
    }
}
