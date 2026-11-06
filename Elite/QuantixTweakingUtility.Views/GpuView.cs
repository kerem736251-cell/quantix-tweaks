using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using QuantixTweakingUtility.Services;
namespace QuantixTweakingUtility.Views;
public sealed class GpuView : UserControl
{
    public GpuView()
    {
        var body = new StackPanel { Margin = new Thickness(0, 18, 14, 12) };
        Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = body };
        var profile = new StackPanel();
        profile.Children.Add(Text("NVIDIA profile", 16, Brushes.White, FontWeights.SemiBold));
        profile.Children.Add(Text("Import the Quantix profile with the verified NVIDIA Profile Inspector release. Review the global driver settings before applying.", 12));
        var settings = new Expander { Header = "View all 28 profile settings", Margin = new Thickness(0, 8, 0, 12) };
        var summary = Text("", 11);
        summary.SetBinding(TextBlock.TextProperty, new Binding("ProfileSummary"));
        settings.Content = summary;
        profile.Children.Add(settings);
        var download = new Button { Content = "Download and import profile", Style = Application.Current.TryFindResource("PrimaryButtonStyle") as Style, HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(16, 9, 16, 9) };
        download.SetBinding(Button.CommandProperty, new Binding("InstallNvidiaProfileCommand"));
        profile.Children.Add(download);
        var status = Text("", 11);
        status.SetBinding(TextBlock.TextProperty, new Binding("NvidiaStatus"));
        profile.Children.Add(status);
        body.Children.Add(new Border { Child = profile, Padding = new Thickness(20), Margin = new Thickness(0, 0, 0, 10), CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), BorderBrush = new SolidColorBrush(Color.FromRgb(45, 36, 60)), Background = new SolidColorBrush(Color.FromRgb(20, 16, 29)) });
        body.Children.Add(new GpuMsiPanel());
        var drivers = new StackPanel { Margin = new Thickness(0, 16, 0, 12) };
        drivers.Children.Add(Text("Clean Driver Installation", 16, Brushes.White, FontWeights.SemiBold));
        drivers.Children.Add(Text("Download the installer for your exact GPU and Windows version first. A clean installation resets driver profiles; save any custom settings you want to keep.", 12));
        drivers.Children.Add(Text("NVIDIA: run the official installer, choose Custom (Advanced), then Perform a clean installation.\nAMD: follow the official Cleanup Utility guide, then install your downloaded Radeon driver. AMD recommends Safe Mode for cleanup.", 12));
        var guides = new StackPanel { Orientation = Orientation.Horizontal };
        AddGuide("NVIDIA installation guide", "https://nvidia.custhelp.com/app/answers/detail/a_id/10");
        AddGuide("AMD cleanup guide", "https://www.amd.com/en/resources/support-articles/faqs/GPU-601.html");
        drivers.Children.Add(guides);
        body.Children.Add(drivers);
        void AddGuide(string label, string address)
        {
            var button = EliteWorkspace.MakeButton(label);
            button.Margin = new Thickness(0, 0, 8, 0);
            button.Click += (_, _) =>
            {
                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(address) { UseShellExecute = true }); }
                catch (System.ComponentModel.Win32Exception) { MessageBox.Show(Window.GetWindow(this), address, label); }
            };
            guides.Children.Add(button);
        }
        var service = new NvidiaSettingsService();
        if (NvidiaSettingsService.LegacyOptions.Any(option => service.HasOriginal(option.Id) && !NvidiaSettingsService.CardOptions.Any(card => card.Id == option.Id)))
            body.Children.Add(new Expander { Header = "Restore settings from a previous Elite version", Foreground = Brushes.LightSteelBlue, Content = new NvidiaSettingsPanel() });
        body.Children.Add(new GameGraphicsPanel());
    }
    private static TextBlock Text(string text, double size, Brush? foreground = null, FontWeight? weight = null) => new()
    {
        Text = text, FontSize = size, Foreground = foreground ?? new SolidColorBrush(Color.FromRgb(174, 167, 193)),
        FontWeight = weight ?? FontWeights.Normal, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 9)
    };
}

