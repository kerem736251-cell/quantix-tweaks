using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using QuantixTweakingUtility.Services;

namespace QuantixTweakingUtility.Views;

internal sealed class FortniteLatencyPanel : UserControl
{
    private readonly FortniteLatencyService service;
    private FortniteEndpoint selected = FortniteLatencyService.Endpoints[0];
    private readonly Button region = EliteWorkspace.MakeButton(string.Empty);
    private readonly Button test = EliteWorkspace.MakeButton("↻");
    private readonly TextBlock measured = new() { Text = "— ms", Foreground = new SolidColorBrush(Color.FromRgb(174, 130, 255)), FontSize = 25, Margin = new Thickness(0, 6, 0, 0) };
    private bool busy;
    private bool hasTested;
    internal ContextMenu RegionMenu => region.ContextMenu;

    public FortniteLatencyPanel(FortniteLatencyService? service = null)
    {
        this.service = service ?? new FortniteLatencyService();
        var body = new StackPanel();
        body.Children.Add(new TextBlock { Text = "FORTNITE PING TEST", Foreground = new SolidColorBrush(Color.FromRgb(174, 167, 193)), FontSize = 10, Margin = new Thickness(0, 0, 0, 5) });
        var picker = new DockPanel();
        test.Width = 26; test.Height = 24; test.Padding = new Thickness(0); test.FontSize = 16;
        test.ToolTip = "Test the selected Epic Games endpoint";
        System.Windows.Automation.AutomationProperties.SetName(test, "Test Fortnite latency");
        test.Click += async (_, _) => await TestSelectedAsync();
        DockPanel.SetDock(test, Dock.Right);
        picker.Children.Add(test);
        region.Height = 24; region.Padding = new Thickness(4, 0, 4, 0); region.FontSize = 10;
        region.Margin = new Thickness(0, 0, 5, 0);
        region.HorizontalContentAlignment = HorizontalAlignment.Left;
        var menu = new ContextMenu { Background = new SolidColorBrush(Color.FromRgb(31, 25, 45)), Foreground = Brushes.White, MaxHeight = 440 };
        menu.PlacementTarget = region;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        menu.Resources[typeof(System.Windows.Controls.Primitives.ScrollBar)] = EliteWorkspace.CreateScrollBarStyle();
        menu.Template = (ControlTemplate)System.Windows.Markup.XamlReader.Parse("""
            <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" TargetType="ContextMenu">
              <Border Background="#1F192D" BorderBrush="#493A60" BorderThickness="1" CornerRadius="6" Padding="4">
                <ScrollViewer CanContentScroll="True" VerticalScrollBarVisibility="Auto" HorizontalScrollBarVisibility="Disabled"
                              Background="#1F192D" Focusable="False">
                  <ItemsPresenter/>
                </ScrollViewer>
              </Border>
            </ControlTemplate>
            """);
        var itemStyle = new Style(typeof(MenuItem));
        itemStyle.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White));
        itemStyle.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
        itemStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(9, 6, 9, 6)));
        itemStyle.Setters.Add(new Setter(Control.FontSizeProperty, 12.0));
        itemStyle.Setters.Add(new Setter(Control.FocusVisualStyleProperty, null));
        itemStyle.Setters.Add(new Setter(Control.TemplateProperty, (ControlTemplate)System.Windows.Markup.XamlReader.Parse("""
            <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="MenuItem">
              <Border x:Name="Row" Background="{TemplateBinding Background}" CornerRadius="4" Padding="{TemplateBinding Padding}">
                <ContentPresenter ContentSource="Header" RecognizesAccessKey="False" VerticalAlignment="Center"/>
              </Border>
              <ControlTemplate.Triggers>
                <Trigger Property="IsHighlighted" Value="True"><Setter TargetName="Row" Property="Background" Value="#493165"/></Trigger>
                <Trigger Property="IsEnabled" Value="False"><Setter Property="Foreground" Value="#8D859F"/></Trigger>
              </ControlTemplate.Triggers>
            </ControlTemplate>
            """)));
        menu.ItemContainerStyle = itemStyle;
        foreach (var endpoint in FortniteLatencyService.Endpoints)
        {
            var choice = new MenuItem { Header = endpoint.Region + " · " + endpoint.Location };
            choice.Click += async (_, _) =>
            {
                if (busy) return;
                selected = endpoint;
                ShowSelection();
                measured.Text = "— ms";
                measured.ToolTip = "Run a test for this endpoint.";
                await TestSelectedAsync();
            };
            menu.Items.Add(choice);
        }
        region.ContextMenu = menu;
        region.Click += (_, _) => menu.IsOpen = true;
        ShowSelection();
        picker.Children.Add(region);
        body.Children.Add(picker);
        body.Children.Add(measured);
        Content = new Border
        {
            Child = body, Background = new SolidColorBrush(Color.FromRgb(27, 24, 36)),
            CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 8, 12, 8)
        };
        ToolTip = "Tests Epic's published datacenter endpoint using ICMP. Matchmaking and gameplay traffic can have different latency. This does not change Fortnite's selected region.";
        Loaded += async (_, _) => { if (!hasTested) await TestSelectedAsync(); };
    }

    private void ShowSelection()
    {
        region.Content = new TextBlock { Text = selected.Region + " · " + selected.Location + " ▾", TextTrimming = TextTrimming.CharacterEllipsis };
        region.ToolTip = selected.Region + " · " + selected.Location + "\n" + selected.Host;
        System.Windows.Automation.AutomationProperties.SetName(region, "Fortnite endpoint: " + selected.Region + ", " + selected.Location);
    }
    internal async Task TestSelectedAsync()
    {
        if (busy) return;
        busy = true;
        hasTested = true;
        test.IsEnabled = region.IsEnabled = false;
        measured.Text = "Testing…";
        measured.FontSize = 20;
        try
        {
            var result = await service.TestAsync(selected);
            measured.Text = result.Average.HasValue ? $"{result.Average:0} ms" : "No reply";
            measured.FontSize = result.Average.HasValue ? 25 : 20;
            measured.ToolTip = result.Summary + " ICMP test only; in-match ping can differ.";
        }
        catch (Exception error)
        {
            measured.Text = "Unavailable";
            measured.FontSize = 18;
            measured.ToolTip = error.Message;
        }
        finally { test.IsEnabled = region.IsEnabled = true; busy = false; }
    }
}
