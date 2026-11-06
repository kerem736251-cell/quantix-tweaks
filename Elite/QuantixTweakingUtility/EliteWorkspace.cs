using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using QuantixTweakingUtility.ViewModels;
using QuantixTweakingUtility.Views;
using QuantixTweakingUtility.Services;
using Microsoft.Extensions.DependencyInjection;

namespace QuantixTweakingUtility;

internal sealed class EliteWorkspace : DockPanel
{
    private static readonly Brush Muted = new SolidColorBrush(Color.FromRgb(174, 167, 193));
    private static readonly Brush Accent = new SolidColorBrush(Color.FromRgb(119, 67, 217));
    private static readonly ControlTemplate ButtonTemplate = (ControlTemplate)System.Windows.Markup.XamlReader.Parse("""
        <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="Button">
          <Border x:Name="Surface" Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}"
                  BorderThickness="{TemplateBinding BorderThickness}" CornerRadius="6" Padding="{TemplateBinding Padding}">
            <ContentPresenter HorizontalAlignment="{TemplateBinding HorizontalContentAlignment}" VerticalAlignment="Center"/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="Surface" Property="BorderBrush" Value="#A780FF"/></Trigger>
            <Trigger Property="IsKeyboardFocused" Value="True"><Setter TargetName="Surface" Property="BorderBrush" Value="#C7AEFF"/></Trigger>
            <Trigger Property="IsPressed" Value="True"><Setter TargetName="Surface" Property="Opacity" Value="0.75"/></Trigger>
            <Trigger Property="IsEnabled" Value="False"><Setter TargetName="Surface" Property="Opacity" Value="0.45"/></Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
        """);
    private static readonly ControlTemplate SelectionTemplate = (ControlTemplate)System.Windows.Markup.XamlReader.Parse("""
        <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="CheckBox">
          <DockPanel Background="Transparent">
            <Grid Width="16" Height="16" Margin="0,0,7,0" VerticalAlignment="Center">
              <Ellipse x:Name="Ring" Stroke="#A398B5" StrokeThickness="1"/>
              <Path x:Name="Tick" Data="M4,8 L7,11 L12,5" Stroke="White" StrokeThickness="1.5" Visibility="Collapsed"/>
            </Grid>
            <ContentPresenter VerticalAlignment="Center"/>
          </DockPanel>
          <ControlTemplate.Triggers>
            <Trigger Property="IsChecked" Value="True">
              <Setter TargetName="Ring" Property="Stroke" Value="White"/>
              <Setter TargetName="Tick" Property="Visibility" Value="Visible"/>
            </Trigger>
            <Trigger Property="IsKeyboardFocused" Value="True">
              <Setter TargetName="Ring" Property="StrokeThickness" Value="2"/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
        """);
    private readonly MainViewModel _model;
    private readonly Window _owner;
    private readonly StackPanel _cards = new();
    private readonly ContentControl _pageBody = new();
    private readonly Dictionary<object, UserControl> _specializedViews = new();
    private GameGraphicsPanel? _gameGraphics;
    private readonly FortniteLatencyPanel _fortniteLatency = new();
    private readonly StackPanel _pageHeading = new();
    private readonly HashSet<TweakRowViewModel> _selected = new();
    private readonly Dictionary<NavItem, Button> _navigation = new();
    private readonly Button _apply = MakeButton(string.Empty);
    private readonly StackPanel _actions = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
    private readonly Button _restore = MakeButton(string.Empty);
    private readonly Button _recommended = MakeButton("Apply recommended");
    private readonly Button _undoRecommended = MakeButton("Revert recommended");
    private readonly HashSet<TweakRowViewModel> _recommendedChanges = new();
    private readonly TextBlock _results = new();

    public EliteWorkspace(MainViewModel model, Window owner)
    {
        Resources[typeof(System.Windows.Controls.Primitives.ScrollBar)] = CreateScrollBarStyle();
        _model = model;
        _owner = owner;
        Background = new SolidColorBrush(Color.FromRgb(14, 12, 21));
        var nav = new StackPanel { Margin = new Thickness(10), Width = 42 };
        nav.Children.Add(new Image { Source = App.EliteLogo(), Width = 26, Height = 26, Margin = new Thickness(0, 1, 0, 18) });
        var primary = new[] { "dashboard", "basic", "processes", "network", "debloat", "gpu", "advanced" };
        foreach (NavItem item in primary.Select(key => model.NavItems.First(item => item.Key == key)))
        {
            var button = MakeButton(string.Empty);
            button.ToolTip = item.Title;
            System.Windows.Automation.AutomationProperties.SetName(button, item.Title);
            _navigation.Add(item, button);
            button.Content = new System.Windows.Shapes.Path
            {
                Data = Geometry.Parse(item.Key switch
                {
                    "dashboard" => "M2,9 L10,2 L18,9 H15 V18 H11 V12 H8 V18 H5 V9 Z",
                    "basic" => "M2,3 L9,2 V9 H2 Z M11,2 L19,1 V9 H11 Z M2,11 H9 V18 L2,17 Z M11,11 H19 V19 L11,18 Z",
                    "power" or "processes" => "M3,18 V11 H6 V18 Z M9,18 V6 H12 V18 Z M15,18 V2 H18 V18 Z",
                    "network" => "M10,2 A8,8 0 1 1 9.99,2 M2,10 H18 M10,2 C5,6 5,14 10,18 M10,2 C15,6 15,14 10,18",
                    "advanced" => "M10,1 L12,7 L18,10 L12,12 L10,19 L8,12 L2,10 L8,7 Z",
                    _ => item.IconGeometry
                }), Fill = item.Key == "network" ? Brushes.Transparent : Muted, Stroke = Muted,
                StrokeThickness = 1, Width = 16, Height = 16, Stretch = Stretch.Uniform
            };
            button.BorderThickness = new Thickness(0);
            button.Height = 36;
            button.Margin = new Thickness(0, 0, 0, 8);
            button.HorizontalContentAlignment = HorizontalAlignment.Center;
            button.Click += (_, _) => model.SelectedNavItem = item;
            nav.Children.Add(button);
        }
        var navScroll = new ScrollViewer { Content = nav, Background = new SolidColorBrush(Color.FromRgb(22, 18, 30)), VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        SetDock(navScroll, Dock.Left);
        Children.Add(navScroll);

        var page = new DockPanel { Margin = new Thickness(16, 12, 16, 10) };
        var actions = _actions;
        _apply.Background = Accent;
        _apply.Click += async (_, _) => await RunAsync(true);
        _restore.Click += async (_, _) => await RunAsync(false);
                _recommended.Click += async (_, _) => await RunRecommendedAsync(true);
        _undoRecommended.Click += async (_, _) => await RunRecommendedAsync(false);
        foreach (var button in new[] { _undoRecommended, _recommended, _restore, _apply })
        {
            button.Padding = new Thickness(11, 4, 11, 4);
            button.Height = 29;
            button.VerticalAlignment = VerticalAlignment.Top;
            button.Margin = new Thickness(8, 0, 0, 10);
            actions.Children.Add(button);
        }
                var toolbar = new DockPanel { Margin = new Thickness(0, 0, 0, 9) };
        DockPanel.SetDock(actions, Dock.Right);
        toolbar.Children.Add(actions);
        toolbar.Children.Add(_pageHeading);
        SetDock(toolbar, Dock.Top);
        page.Children.Add(toolbar);
        _results.Foreground = Muted;
        _results.TextWrapping = TextWrapping.Wrap;

        _results.ToolTip = "Results from the most recent operation";
        var report = new ScrollViewer
        {
            Content = _results, MaxHeight = 100,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        SetDock(report, Dock.Bottom);
        page.Children.Add(report);
        page.Children.Add(_pageBody);
        Children.Add(page);

        model.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.CurrentViewModel))
                RenderPage();
            if (e.PropertyName == nameof(MainViewModel.SelectedNavItem))
                HighlightNavigation();
        };
        RenderPage();
        HighlightNavigation();
    }

    private void HighlightNavigation()
    {
        foreach (var (item, button) in _navigation)
        {
            button.Background = item == _model.SelectedNavItem
                ? new SolidColorBrush(Color.FromRgb(45, 32, 65))
                : Brushes.Transparent;
            if (button.Content is System.Windows.Shapes.Path icon)
            {
                var color = item == _model.SelectedNavItem ? new SolidColorBrush(Color.FromRgb(180, 137, 255)) : Muted;
                icon.Stroke = color;
                if (item.Key != "network") icon.Fill = color;
            }
        }
    }

    private TweakTabViewModel? CurrentTab => _model.CurrentViewModel switch
    {
        GpuViewModel gpu => gpu.Tab,
        TweakTabViewModel tab => tab,
        _ => null
    };

    private void RenderPage()
    {
        if (_pageBody.Content is ScrollViewer previous)
            previous.Content = null;
        _pageBody.Content = null;
        _selected.Clear();
        _cards.Children.Clear();
        _pageHeading.Children.Clear();
        _results.Text = string.Empty;
        UpdateActions();
        _actions.Visibility = CurrentTab != null ? Visibility.Visible : Visibility.Collapsed;
        if (_model.CurrentViewModel is DashboardViewModel || CurrentTab != null)
            _pageBody.Content = new ScrollViewer { Content = _cards, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        else if (_model.CurrentViewModel is { } specialized)
        {
            if (!_specializedViews.TryGetValue(specialized, out var view))
            {
                view = specialized switch
                {

                    GamingViewModel => new GamingView(),
                    CleanupViewModel => new CleanupView(),
                    StartupManagerViewModel => new StartupManagerView(),
                    BiosViewModel => new BiosView(),
                    SettingsViewModel => new SettingsView(),
                    _ => throw new InvalidOperationException("Unsupported Elite page.")
                };
                view.DataContext = specialized;
                _specializedViews.Add(specialized, view);
            }
            _pageBody.Content = view;
            return;
        }
        if (_model.CurrentViewModel is DashboardViewModel dashboard)
        {
            RenderDashboard(dashboard);
            return;
        }
        if (CurrentTab is not { } tab)
            return;

        _pageHeading.Children.Add(Text(tab.Key switch { "basic" => "Windows Optimization", "gpu" => "GPU Optimization", "network" => "Network Optimization", "power" => "Power Optimization", _ => tab.Title }, 24, Brushes.White));
        _pageHeading.Children.Add(Text(tab.Description, 11, Muted));
        if (_model.CurrentViewModel is GpuViewModel gpu)
        {
            var tools = MakeButton("Graphics tools and driver controls");
            tools.HorizontalAlignment = HorizontalAlignment.Left;
            tools.Click += (_, _) => OpenGraphicsTools();
            _cards.Children.Add(tools);
        }
        if (tab.Key == "advanced")
        {
            var games = MakeButton("Windowed game optimizations and GPU preference  →");
            games.HorizontalAlignment = HorizontalAlignment.Left;
            games.ToolTip = "Choose a game's executable and configure its supported Windows graphics preferences.";
            games.Click += (_, _) => OpenDetail(_gameGraphics ??= new GameGraphicsPanel(), "Back to Extra Optimization");
            _cards.Children.Add(games);
        }
        foreach (var group in GroupsFor(tab))
        {
            var groupName = System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(group.Name.ToLowerInvariant());
            foreach (var acronym in new[] { "Cpu", "Gpu", "Usb", "Dns", "Tcp", "Nvidia", "Amd", "Ram", "Dvr", "Hdr", "Bcd", "Msi", "Kbm" })
                groupName = groupName.Replace(acronym, acronym.ToUpperInvariant());
            var heading = Text(groupName, 13, Brushes.White);
            heading.FontWeight = FontWeights.SemiBold;
            heading.Margin = new Thickness(0, 16, 0, 8);
            var section = new DockPanel { Margin = new Thickness(28, 14, 0, 8) };
            var marker = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(45, 31, 68)),
                CornerRadius = new CornerRadius(5), Width = 24, Height = 24,
                Margin = new Thickness(0, 0, 9, 0),
                Child = new System.Windows.Shapes.Path
                {
                    Data = Geometry.Parse("M11,2 L5,11 H10 L9,18 L16,8 H11 Z"),
                    Fill = Brushes.Lavender, Stretch = Stretch.Uniform, Width = 11, Height = 14
                }
            };
            DockPanel.SetDock(marker, Dock.Left);
            section.Children.Add(marker);
            heading.Margin = new Thickness(0);
            heading.VerticalAlignment = VerticalAlignment.Center;
            section.Children.Add(heading);
            _cards.Children.Add(section);
            var grid = new Grid { Margin = new Thickness(28, 0, 0, 0) };
            for (int column = 0; column < 3; column++) grid.ColumnDefinitions.Add(new ColumnDefinition());
            for (int index = 0; index < group.Rows.Count; index++)
            {
                if (index % 3 == 0) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var card = CreateCard(group.Rows[index]);
                Grid.SetRow(card, index / 3); Grid.SetColumn(card, index % 3);
                grid.Children.Add(card);
            }
            _cards.Children.Add(grid);
        }
    }

    internal void OpenGraphicsTools()
    {
        if (_model.CurrentViewModel is not GpuViewModel gpu) return;

                if (!_specializedViews.TryGetValue(gpu, out var view))
                {
                    view = new GpuView { DataContext = gpu };
                    _specializedViews.Add(gpu, view);
                }
                OpenDetail(view, "Back to GPU Optimization");
    }

    private void OpenDetail(UserControl view, string backLabel)
    {
                if (_pageBody.Content is ScrollViewer previous) previous.Content = null;
                var layout = new DockPanel();
                var back = MakeButton(backLabel);
                back.HorizontalAlignment = HorizontalAlignment.Left;
                back.Click += async (_, _) =>
                {
                    layout.Children.Remove(view);
                    RenderPage();
                    if (CurrentTab is { } tab)
                        foreach (var row in GroupsFor(tab).SelectMany(group => group.Rows))
                            if (!row.IsBusy) await row.RefreshStateAsync();
                };
                DockPanel.SetDock(back, Dock.Top);
                layout.Children.Add(back);
                if (view.Parent is Panel formerLayout)
                    formerLayout.Children.Remove(view);
                layout.Children.Add(view);
                _actions.Visibility = Visibility.Collapsed;
                _pageBody.Content = layout;
            
    }
    internal System.Threading.Tasks.Task WaitForCurrentPageAsync() => CurrentTab is { } tab
        ? System.Threading.Tasks.Task.WhenAll(GroupsFor(tab).SelectMany(group => group.Rows).Select(row => row.Initialization))
        : System.Threading.Tasks.Task.CompletedTask;

    internal string[] CurrentStateReport() => CurrentTab is { } tab
        ? GroupsFor(tab).SelectMany(group => group.Rows).Select(row =>
            row.Definition.Id + "\t" + row.Definition.Name + "\t" + row.StateLabel + "\t" + row.StatusText.Replace('\r', ' ').Replace('\n', ' ')).ToArray()
        : Array.Empty<string>();

    private IEnumerable<TweakGroupViewModel> GroupsFor(TweakTabViewModel tab) =>
        tab.Key switch
        {
            "basic" => new[] { MergeWindowsGroups(tab), MergePerformanceGroups(tab) }
                .Concat(new[] { MergePowerGroups(_model.PowerTab) }).Concat(_model.AppearanceTab.Groups)
                .Concat(tab.Groups.Where(group=>group.Name=="USB TWEAKS"))
                .Concat(new[]{MergeBootGroups(tab)})
                .Concat(tab.Groups.Where(group => group.Name == "CLEANER"))
                .Concat(new[] { MergeExtraWindowsGroups(tab) })
                .Concat(new[] { MergeStorageGroups(tab) })
                .Concat(new[] { "RAM TWEAKS", "DISC ERRORS", "GAME DVR" }.SelectMany(name => tab.Groups.Where(group => group.Name == name))),
            "advanced" => MergeAdvancedGroups(tab),
            "debloat" => _model.ServicesTab.Groups.Concat(new[] { MergeBrowserGroups(tab) }),
            "network" => MergeNetworkGroups(tab),
            "power" => new[] { MergePowerGroups(tab) },
            _ => tab.Groups
        };

    private static TweakGroupViewModel MergeExtraWindowsGroups(TweakTabViewModel tab)
    {
        var group=new TweakGroupViewModel { Name="EXTRA TWEAKS" };
        var order=new[] { "windows.audio-ducking.disable", "windows.uac.disable", "windows.explorer.single-process", "appearance.classic-alt-tab.enable" };
        foreach(var row in tab.Groups.Where(item=>item.Name=="EXTRA TWEAKS").SelectMany(item=>item.Rows)
            .OrderBy(row=>{int index=Array.IndexOf(order,row.Definition.Id);return index<0?int.MaxValue:index;}))group.Rows.Add(row);
        return group;
    }

    private IEnumerable<TweakGroupViewModel> MergeAdvancedGroups(TweakTabViewModel tab)
    {
        var keyboardMouse=new TweakGroupViewModel { Name="KBM TWEAKS" };
        var games=new TweakGroupViewModel { Name="GAME TWEAKS" };
        var additional=new TweakGroupViewModel { Name="ADDITIONAL TWEAKS" };
        var pointer=new TweakGroupViewModel { Name="POINTER OPTIONS" };
        var primary=new[] { "input.keyboard-queue.200", "input.mouse-queue.200", "gaming.mouse-acceleration.disable", "input.keyboard-response.fast" };
        var all=tab.Groups.SelectMany(group=>group.Rows).Concat(_model.PrecisionTab.Groups.SelectMany(group=>group.Rows)).ToArray();
        foreach(string id in primary)foreach(var row in all.Where(row=>row.Definition.Id==id))keyboardMouse.Rows.Add(row);
        foreach(var row in all.Where(row=>!primary.Contains(row.Definition.Id)))
        {
            if(row.Definition.Id.StartsWith("game.",StringComparison.Ordinal))games.Rows.Add(row);
            else if(row.Definition.Id.StartsWith("precision.",StringComparison.Ordinal))pointer.Rows.Add(row);
            else additional.Rows.Add(row);
        }
        return new[] { games,keyboardMouse,additional,pointer }.Where(group=>group.Rows.Count>0);
    }

    private static TweakGroupViewModel MergeStorageGroups(TweakTabViewModel tab)
    {
        var group=new TweakGroupViewModel { Name="STORAGE TWEAKS" };
        var order=new[] { "windows.gpu-tdr.disable", "storage.trim.enable", "storage.nvme-msi.enable", "storage.optimize-system-drive" };
        foreach(var row in tab.Groups.Where(item=>item.Name=="STORAGE TWEAKS").SelectMany(item=>item.Rows)
            .OrderBy(row=>{int index=Array.IndexOf(order,row.Definition.Id);return index<0?int.MaxValue:index;}))group.Rows.Add(row);
        return group;
    }

    internal static Style CreateScrollBarStyle()
    {
        var scrollbarStyle = new Style(typeof(System.Windows.Controls.Primitives.ScrollBar));
        scrollbarStyle.Setters.Add(new Setter(WidthProperty, 8.0));
        scrollbarStyle.Setters.Add(new Setter(BackgroundProperty, Brushes.Transparent));
        scrollbarStyle.Setters.Add(new Setter(Control.ForegroundProperty,
            new SolidColorBrush(Color.FromRgb(73, 58, 96))));
        scrollbarStyle.Setters.Add(new Setter(Control.TemplateProperty,
            (ControlTemplate)System.Windows.Markup.XamlReader.Parse("""
            <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="ScrollBar">
              <Grid Background="Transparent">
                <Track x:Name="PART_Track" IsDirectionReversed="True" Orientation="{TemplateBinding Orientation}"
                       Minimum="{TemplateBinding Minimum}" Maximum="{TemplateBinding Maximum}"
                       ViewportSize="{TemplateBinding ViewportSize}"
                       Value="{Binding Value, RelativeSource={RelativeSource TemplatedParent}, Mode=TwoWay}">
                  <Track.DecreaseRepeatButton>
                    <RepeatButton Command="ScrollBar.PageUpCommand" Opacity="0" Focusable="False"/>
                  </Track.DecreaseRepeatButton>
                  <Track.Thumb>
                    <Thumb Background="#493A60">
                      <Thumb.Template>
                        <ControlTemplate TargetType="Thumb">
                          <Border Background="{TemplateBinding Background}" CornerRadius="4" Margin="1"/>
                        </ControlTemplate>
                      </Thumb.Template>
                    </Thumb>
                  </Track.Thumb>
                  <Track.IncreaseRepeatButton>
                    <RepeatButton Command="ScrollBar.PageDownCommand" Opacity="0" Focusable="False"/>
                  </Track.IncreaseRepeatButton>
                </Track>
              </Grid>
            </ControlTemplate>
            """)));
        return scrollbarStyle;
    }

    private static TweakGroupViewModel MergeBootGroups(TweakTabViewModel tab)
    {
        var group=new TweakGroupViewModel { Name="BCD-EDIT" };
        var order=new[]{"boot.platform-clock-tsc","power.dynamic-tick.disable","boot.x2apic.enable"};
        foreach(var row in tab.Groups.Where(g=>g.Name=="BCD-EDIT").SelectMany(g=>g.Rows).OrderBy(row=>Array.IndexOf(order,row.Definition.Id)))group.Rows.Add(row);
        return group;
    }

    private static TweakGroupViewModel MergeBrowserGroups(TweakTabViewModel tab)
    {
        var group=new TweakGroupViewModel { Name="BROWSER & APPS" };
        var order=new[]{"debloat.chrome-background.disable","debloat.edge-background.disable","debloat.browser-updaters.disable","debloat.chrome-reporting.disable","debloat.firefox-telemetry.disable"};
        foreach(var row in tab.Groups.SelectMany(item=>item.Rows).OrderBy(row=>{int index=Array.IndexOf(order,row.Definition.Id);return index<0?int.MaxValue:index;}))group.Rows.Add(row);
        return group;
    }

    private static TweakGroupViewModel MergePerformanceGroups(TweakTabViewModel tab)
    {
        var group = new TweakGroupViewModel { Name = "PERFORMANCE TWEAKS" };
        var order = new[]
        {
            "windows.timer-resolution.1ms", "windows.fault-tolerant-heap.disable", "gaming.mmcss-priority.boost", "windows.fast-shutdown", "windows.mmcss.audio-window", "mitigations.cpu-side-channel.disable",
            "mitigations.vbs.disable", "mitigations.hvci.disable", "windows.kernel-memory-policy", "perf.power-throttling.disable", "windows.onedrive-sync.disable",
            "windows.resource-policy", "windows.insider-builds.disable", "advanced.search-indexing-service.disable", "windows.error-reporting.disable",
            "advanced.sysmain-service.disable", "windows.prefetch.boot-only", "windows.game-content-indexing.disable"
        };
        foreach (var row in tab.Groups.Where(item => item.Name == "PERFORMANCE TWEAKS").SelectMany(item => item.Rows)
            .OrderBy(row => { int index = Array.IndexOf(order, row.Definition.Id); return index < 0 ? int.MaxValue : index; }))
            group.Rows.Add(row);
        return group;
    }

    private static TweakGroupViewModel MergePowerGroups(TweakTabViewModel tab)
    {
        var group = new TweakGroupViewModel { Name = "POWER TWEAKS" };
        var order = new[]
        {
            "cpu.power-plan.quantix-elite", "gaming.game-mode.enable", "power.hibernation.disable",
            "windows.fast-startup.disable", "power.policy.cpu-epp", "power.policy.cpu-boost",
            "power.policy.cpu-ramp", "power.policy.cpu-idle", "power.pcie-aspm.disable", "power.policy.adaptive-brightness",
            "perf.usb-selective-suspend", "power.policy.never-sleep-ac", "power.network.task-offload.disable", "power.network.transition-tunnels.disable", "power.network.timestamps.disable"
        };
        foreach (var row in tab.Groups.SelectMany(item => item.Rows)
            .OrderBy(row => { int index = Array.IndexOf(order, row.Definition.Id); return index < 0 ? int.MaxValue : index; }))
            group.Rows.Add(row);
        return group;
    }

    private static IEnumerable<TweakGroupViewModel> MergeNetworkGroups(TweakTabViewModel tab)
    {
        var group = new TweakGroupViewModel { Name = "NETWORK TWEAKS" };
        var adapters = new TweakGroupViewModel { Name = "ADAPTER TWEAKS" };
        var wifi = new TweakGroupViewModel { Name = "WIFI TWEAKS" };
        var order = new[]
        {
            "network.adapter.buffers", "network.adapter.rsc.disable", "network.adapter.lso.disable",
            "network.wifi.power.maximum", "network.wifi.uapsd.disable", "network.wifi.mimo-power-save.disable",
            "network.wifi.prefer-5ghz", "network.wifi.roaming.maximum", "network.wifi.transmit.maximum",
            "network.adapter-rss.enable", "advanced.nagle-algorithm.disable", "network.adapter.eee",
            "network.tcp.time-wait.30",
            "network.tcp-window-heuristics.disable", "network.delivery-optimization.disable", "network.cloudflare-dns.ipv4",
            "network.flush-dns-renew", "network.stack.reset", "network.adapter.interrupt-moderation", "network.adapter.buffers"
        };
        foreach (var row in tab.Groups.SelectMany(item => item.Rows)
            .OrderBy(row => { int index = Array.IndexOf(order, row.Definition.Id); return index < 0 ? int.MaxValue : index; }))
        {
            if (row.Definition.Id.StartsWith("network.wifi.", StringComparison.Ordinal)) wifi.Rows.Add(row);
            else if (row.Definition.Id.StartsWith("network.adapter.", StringComparison.Ordinal) || row.Definition.Id == "network.adapter-rss.enable")
                adapters.Rows.Add(row);
            else group.Rows.Add(row);
        }
        return new[] { group, adapters, wifi }.Where(item => item.Rows.Count > 0);
    }

    private static TweakGroupViewModel MergeWindowsGroups(TweakTabViewModel tab)
    {
        var group = new TweakGroupViewModel { Name = "WINDOWS SETTINGS" };
        var order = new[]
        {
            "perf.visual-effects.best-performance", "windows.tips-suggestions.disable",
            "windows.search-web-suggestions.disable", "windows.recall-snapshots.disable",
            "windows.widgets-news.disable", "windows.menu-delay.reduce", "windows.aero-shake.disable", "windows.long-paths.enable",
            "windows.crash-auto-restart.disable", "windows.automatic-updates.disable",
            "windows.update-pause.35days",

            "network.throttling-index.disable",
            "perf.background-apps.disable", "windows.clipboard-history.disable"
        };
        foreach (var row in tab.Groups.Where(item => item.Name is not ("PERFORMANCE TWEAKS" or "BCD-EDIT" or "CLEANER" or "EXTRA TWEAKS" or "STORAGE TWEAKS" or "USB TWEAKS" or "RAM TWEAKS" or "DISC ERRORS" or "GAME DVR")).SelectMany(item => item.Rows)
            .OrderBy(row => { int index = Array.IndexOf(order, row.Definition.Id); return index < 0 ? int.MaxValue : index; }))
            group.Rows.Add(row);
        return group;
    }

    private void RenderDashboard(DashboardViewModel dashboard)
    {
        var overview = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        overview.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3.4, GridUnitType.Star) });
        overview.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var left = new StackPanel { Margin = new Thickness(0, 0, 10, 0) };
        var hardware = new StackPanel();
        hardware.Children.Add(Text("THIS PC", 10, Muted));
        foreach (var (label, property) in new[]
        {
            ("CPU", nameof(DashboardViewModel.CpuName)),
            ("GPU", nameof(DashboardViewModel.GpuName)),
            ("RAM", nameof(DashboardViewModel.RamTotalDisplay))
        })
        {
            var row = new DockPanel();
            var name = Text(label, 11, Muted);
            name.Width = 56;
            DockPanel.SetDock(name, Dock.Left);
            row.Children.Add(name);
            var value = Text(string.Empty, 11, Brushes.White);
            value.SetBinding(TextBlock.TextProperty, new Binding(property) { Source = dashboard });
            row.Children.Add(value);
            hardware.Children.Add(row);
        }
        left.Children.Add(DashboardTile(hardware));
        var chart = new EliteCpuChart { Margin = new Thickness(0), Height = 225 };
        var chartLayout = new Grid { Margin = new Thickness(0, 8, 0, 0) };
        chartLayout.Children.Add(chart);
        var metrics = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(14, 10, 0, 0) };
        Button? activeMetric = null;
        foreach (var metric in Enum.GetValues<EliteCpuChart.Metric>())
        {
            var button = MakeButton(metric.ToString().ToUpperInvariant());
            button.Margin = new Thickness(0, 0, 6, 0);
            var inactive = button.Background;
            if (metric == EliteCpuChart.Metric.Cpu)
            {
                activeMetric = button;
                button.Background = Accent;
            }
            button.Click += (_, _) =>
            {
                if (activeMetric != null)
                    activeMetric.Background = inactive;
                chart.SelectMetric(metric);
                button.Background = Accent;
                activeMetric = button;
            };
            metrics.Children.Add(button);
        }
        chartLayout.Children.Add(metrics);
        left.Children.Add(chartLayout);
        overview.Children.Add(left);
        var status = new StackPanel();
        Grid.SetColumn(status, 1);
        foreach (var (label, value) in new[]
        {
            ("CPU TEMPERATURE", "Unavailable")
        })
        {
            var content = new StackPanel();
            content.Children.Add(Text(label, 10, Muted));
            content.Children.Add(Text(value, 16, Brushes.Lavender));
            var tile = DashboardTile(content);
            tile.Margin = new Thickness(0, 0, 0, 8);
            status.Children.Add(tile);
        }
        if (_fortniteLatency.Parent is Panel previousStatus) previousStatus.Children.Remove(_fortniteLatency);
        _fortniteLatency.Margin = new Thickness(0, 0, 0, 8);
        status.Children.Add(_fortniteLatency);
        var ready = new StackPanel();
        ready.Children.Add(Text("STATUS", 10, Muted));
        var loadStatus = Text("WAITING", 16, Brushes.Lavender);
        loadStatus.ToolTip = "Selected chart metric only: High Load at 90% or above. This is not a temperature or overall system-health assessment.";
        chart.SampleUpdated += (metric, value) =>
        {
            loadStatus.Text = MetricLoadStatus.Describe(value);
            loadStatus.ToolTip = metric.ToString().ToUpperInvariant() + " utilization: High Load at 90% or above. This does not assess temperature or overall system health.";
        };
        ready.Children.Add(loadStatus);
        status.Children.Add(DashboardTile(ready));
        overview.Children.Add(status);
        _cards.Children.Add(overview);
        _cards.Children.Add(Text("Quantix Support", 16, Brushes.White));
        _cards.Children.Add(Text("Help, updates, and the Quantix community", 11, Muted));
        var links = new System.Windows.Controls.Primitives.UniformGrid { Columns = 3, Margin = new Thickness(0, 8, 0, 0) };
        foreach (var (icon, label, subtitle, address) in new[]
        {
            ("quantix", "Official website", "quantixtweaks.com", "https://quantixtweaks.com"),
            ("discord", "Discord", "Join the Quantix community", "https://discord.gg/c7bVkahGaM"),
            ("tiktok", "TikTok", "@quantix_tweaks", "https://www.tiktok.com/@quantix_tweaks"),
            ("x", "X / Twitter", "@quantixfnr", "https://x.com/quantixfnr"),
            ("discord", "Support", "Get help in our Discord", "https://discord.gg/c7bVkahGaM"),
            ("quantix", "Quantix Elite", "Updates and product information", "https://quantixtweaks.com")
        })
        {
            var button = MakeButton(string.Empty);
            var content = new DockPanel();
            var symbol = new Border
            {
                Width = 36, Height = 30, VerticalAlignment = VerticalAlignment.Center,
                Child = BrandMarks.Create(icon, icon == "quantix" ? 28 : 22)
            };
            DockPanel.SetDock(symbol, Dock.Left);
            content.Children.Add(symbol);
            var copy = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var title = Text(label, 12, Brushes.White);
            title.FontWeight = FontWeights.SemiBold;
            copy.Children.Add(title);
            copy.Children.Add(Text(subtitle, 10, Muted));
            content.Children.Add(copy);
            button.Content = content;
            button.HorizontalContentAlignment = HorizontalAlignment.Left;
            button.Margin = new Thickness(0, 0, 8, 8);
            button.Padding = new Thickness(14, 8, 14, 8);
            button.Height = 62;
            button.Click += (_, _) =>
            {
                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(address) { UseShellExecute = true }); }
                catch (System.ComponentModel.Win32Exception) { MessageBox.Show(_owner, address, label); }
            };
            links.Children.Add(button);
        }
        _cards.Children.Add(links);
    }

    private static Border DashboardTile(UIElement content) => new()
    {
        Background = new SolidColorBrush(Color.FromRgb(27, 24, 36)),
        CornerRadius = new CornerRadius(8),
        Padding = new Thickness(12, 8, 12, 8),
        Child = content
    };

    private Border CreateCard(TweakRowViewModel row)
    {
        var card = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(31, 25, 45)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(62, 50, 83)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(10),
            Margin = new Thickness(0, 0, 8, 8),
            MinHeight = 84
        };
        var stack = new StackPanel();
        card.Cursor = System.Windows.Input.Cursors.Hand;
        var name = Text(row.Definition.Name, 12, Brushes.White);
        name.MaxHeight = 32; name.TextTrimming = TextTrimming.CharacterEllipsis;
        var pick = new CheckBox { Template = SelectionTemplate, Content = name, VerticalContentAlignment = VerticalAlignment.Center };
        pick.SetBinding(IsEnabledProperty, new Binding(nameof(TweakRowViewModel.CanSelect)) { Source = row });
        var description = Text(CardSummary(row.Definition.Id) ?? row.Definition.Description, 11, Muted);
        card.MouseEnter += (_, _) =>
        {
            if (pick.IsEnabled && pick.IsChecked != true)
                card.BorderBrush = new SolidColorBrush(Color.FromRgb(139, 101, 194));
        };
        card.MouseLeave += (_, _) =>
        {
            if (pick.IsChecked != true)
                card.BorderBrush = new SolidColorBrush(Color.FromRgb(62, 50, 83));
        };
        description.MaxHeight = 72;
        description.TextTrimming = TextTrimming.CharacterEllipsis;
        description.ToolTip = row.Definition.Description;
        pick.Checked += (_, _) => { _selected.Add(row); card.BorderBrush = Accent; card.Background = Accent; description.Foreground = Brushes.White; UpdateActions(); };
        pick.Unchecked += (_, _) => { _selected.Remove(row); card.BorderBrush = new SolidColorBrush(Color.FromRgb(62, 50, 83)); card.Background = new SolidColorBrush(Color.FromRgb(31, 25, 45)); description.Foreground = Muted; UpdateActions(); };
                var top = new DockPanel();
        var risk = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(54, 44, 72)),
            CornerRadius = new CornerRadius(4), Padding = new Thickness(5, 1, 5, 1),
            VerticalAlignment = VerticalAlignment.Top,
            Child = Text((row.Definition.RiskLevel == QuantixTweakingUtility.Models.TweakRiskLevel.Medium ? "MED" : row.Definition.RiskLevel.ToString().ToUpperInvariant()), 9, Brushes.Lavender)
        };
        DockPanel.SetDock(risk, Dock.Right);
        top.Children.Add(risk);
        top.Children.Add(pick);
        stack.Children.Add(top);
        card.MouseLeftButtonUp += (_, e) =>
        {
            if (!e.Handled && pick.IsEnabled) pick.IsChecked = pick.IsChecked != true;
        };
        stack.Children.Add(description);
        var status = Text(string.Empty, 9, Muted);
        status.Margin = new Thickness(0, 2, 0, 0);
        status.SetBinding(TextBlock.TextProperty, new Binding(nameof(TweakRowViewModel.StateLabel)) { Source = row });
        stack.Children.Add(status);
        card.Child = stack;
        return card;
    }

        private async System.Threading.Tasks.Task RunRecommendedAsync(bool apply)
    {
        if (CurrentTab is not { } tab)
            return;
        _recommended.IsEnabled = _undoRecommended.IsEnabled = false;
        try
        {
            var pageRows = GroupsFor(tab).SelectMany(group => group.Rows);
            if (apply)
            {
                var service = App.Services.GetRequiredService<ITweakService>();
                var ids = (await service.GetRecommendedTweaksAsync()).Select(tweak => tweak.Id).ToHashSet();
                var candidates = pageRows.Where(row => ids.Contains(row.Definition.Id)).ToArray();
                await RunAsync(true, candidates);
                foreach (var row in candidates.Where(row => row.IsApplied))
                    _recommendedChanges.Add(row);
            }
            else
            {
                var candidates = pageRows.Where(_recommendedChanges.Contains).ToArray();
                await RunAsync(false, candidates);
                _recommendedChanges.RemoveWhere(row => !row.IsApplied);
            }
        }
        catch (Exception error)
        {
            _results.Text = error.Message;
        }
        finally
        {
            UpdateActions();
        }
    }

    private async System.Threading.Tasks.Task RunAsync(bool apply, TweakRowViewModel[]? batch = null)
    {
        var rows = (batch ?? _selected.ToArray()).OrderBy(row => BatchOperationOrder.Priority(row.Definition.Id, apply)).ToArray();
        if (rows.Length == 0)
        {
            _results.Text = "No applicable tweaks on this page.";
            return;
        }

        try
        {
            Action<int, string>? report = null;
            await App.ShowProgressAsync(apply ? "Applying Tweaks" : "Restoring Tweaks",
                $"{rows.Length} selected", async () =>
                {
                    for (int index = 0; index < rows.Length; index++)
                    {
                        var row = rows[index];
                        if (apply && BatchOperationOrder.IsPlanSpecific(row.Definition.Id)
                            && rows.Any(item => item.Definition.Id == BatchOperationOrder.PowerPlan && !item.IsApplied))
                        {
                            row.StatusText = "Skipped because the selected power plan was not activated.";
                            continue;
                        }
                        if (!apply && row.Definition.Id == BatchOperationOrder.PowerPlan
                            && rows.Any(item => BatchOperationOrder.IsPlanSpecific(item.Definition.Id) && item.Definition.HasSavedOriginal?.Invoke() == true))
                        {
                            row.StatusText = "Previous power plan was not activated because selected plan-specific originals still need restoration. Retry those settings first.";
                            continue;
                        }
                        report?.Invoke(index, $"{(apply ? "Applying" : "Restoring")} {row.Definition.Name}");
                        await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.Background);
                        await row.ChangeAsync(apply);
                        if (CurrentTab is { } tab)
                        {
                            var affected = row.Definition.AffectedSettings.ToHashSet(StringComparer.OrdinalIgnoreCase);
                            foreach (var related in GroupsFor(tab).SelectMany(group => group.Rows))
                            {
                                if (related != row && (related.Definition.AffectedSettings.Any(affected.Contains)
                                    || row.Definition.Id == BatchOperationOrder.PowerPlan && BatchOperationOrder.IsPlanSpecific(related.Definition.Id)
                                    || row.Definition.Id == "network.stack.reset" && related.Definition.Category == QuantixTweakingUtility.Models.TweakCategory.Network))
                                    await related.RefreshStateAsync();
                            }
                        }
                    }
                    report?.Invoke(rows.Length, $"{rows.Length} / {rows.Length} processed");
                }, _owner, rows.Length, update => report = update);
            _results.Text = string.Join(Environment.NewLine, rows.Select(row => $"{row.Definition.Name}: {row.StatusText}"));
        }
        catch (Exception error)
        {
            _results.Text = error.Message;
        }
    }

    private static string? CardSummary(string id) => id switch
    {
        "processes.startup-quarantine" => "Moves supported third-party startup entries into a verified quarantine. Restore returns the exact original commands and registry types.",
        "processes.task-scheduler-firewall" => "Disables third-party scheduled tasks and saves their original state. Skips Microsoft task folders and registered Microsoft authors.",
        "processes.compatibility.disable" => "Disables Program Compatibility Assistant and Windows Font Cache. May affect older applications and font loading.",
        "processes.delivery-service.disable" => "Disables the Delivery Optimization service that manages peer-to-peer Windows update downloads.",
        "processes.biometric.disable" => "Disables the Windows biometric service. Fingerprint and facial-recognition sign-in become unavailable.",
        "processes.vendor-services.disable" => "Disables supported Razer, Samsung connection, Canon survey and AMD Crash Defender services. Original service settings are saved for restore.",
        "processes.cleanup.browsers" => "Stops leftover Edge, Chrome, Brave, Opera and Firefox processes after their windows close. Saved sign-ins remain on disk.",
        "processes.cleanup.autologger" => "Disables SQM and application telemetry boot logging, and stops matching active trace sessions. Restore returns the original boot settings.",
        "processes.cleanup.bloat" => "Stops OneDrive, Widgets, Game Bar, Phone Link, Teams and Cortana for this session. Apps may restart themselves.",
        "windows.repair.sfc" => "Verifies and repairs protected Windows system files. Takes several minutes; the Windows result is saved to a log.",
        "windows.repair.restore-health" => "Repairs the Windows component store using DISM. May download repair content; automatic restart stays off.",
        "windows.fault-tolerant-heap.disable" => "Disables crash-compatibility heap mitigations for new apps after restart. Affected software may become less stable.",
        "windows.aero-shake.disable" => "Stops shake-to-minimize so moving a window does not collapse the others. Sign out or restart Explorer to refresh.",
        "windows.insider-builds.disable" => "Blocks future Insider preview builds through Windows policy. Your current Windows build stays installed.",
        "windows.fast-shutdown" => "Shortens the service shutdown wait to two seconds. Unfinished service writes can be lost; this does not change application save prompts.",
        "windows.timer-resolution.1ms" => "Requests a 1ms timer period for Elite while it stays open. Restore or exit releases it. Modern Windows scopes this request to Elite's process.",
        "windows.resource-policy" => "Matches the service-host split threshold to installed RAM. Can group compatible services after restart, reducing their isolation.",
        "windows.explorer.single-process" => "Uses the shared File Explorer process for folder windows. Sign out or restart Explorer to refresh; Alt-Tab styling stays unchanged.",
        "gpu.windows-telemetry-tasks.disable" => "Disables selected Windows compatibility/CEIP diagnostic task switches. Can affect upgrade-readiness data; existing running tasks keep running. Saves originals.",
        "game.discord-hardware-acceleration.disable" => "Disables Discord desktop GPU acceleration. Fully quit Discord before applying or restoring; reopen afterward. May increase CPU use or slow Discord rendering.",
        "boot.platform-clock-tsc" => "Stops forcing a platform clock and sets TSC sync policy to Enhanced. Debugging options; may cause timing or startup problems. Restart required.",
        "boot.x2apic.enable" => "Requests x2APIC policy Enable when the CPU reports support. Hardware/firmware compatibility still matters; startup problems are possible. Restart required.",
        "network.pci-msi.enable" => "Requests MSI on supported wired PCI network adapters. Driver incompatibility can disrupt networking. Saves original values; restart required.",
        "input.usb-controller-msi.enable" => "Requests MSI on supported PCI USB host controllers. Driver incompatibility can affect USB input and devices. Saves originals; restart required.",
        "storage.nvme-msi.enable" => "Requests MSI on supported PCI NVMe controllers. Driver incompatibility can cause storage or boot problems. Saves originals; restart required.",
        "debloat.browser-updaters.disable" => "Stops and disables machine Edge/Google updater services. Blocks service-based security updates and can affect WebView2; restore before updating.",
        "input.keyboard-queue.200" => "Requests a 200-event keyboard class-driver buffer. May help buffer overflow; does not change polling rate or hardware rollover. Restart required.",
        "input.mouse-queue.200" => "Requests a 200-event mouse class-driver buffer. Uses more buffer memory; does not directly lower input latency or change polling rate. Restart required.",
        "windows.uac.disable" => "Disables UAC and Admin Approval Mode. Weakens Windows security; not an FPS optimization. Saves the original value. Restart required.",
        "windows.gpu-tdr.disable" => "Disables GPU timeout detection for driver debugging. A GPU hang may freeze the desktop and require a forced restart. Not a normal performance tweak.",
        "network.stack.reset" => "Flushes DNS and resets Winsock/TCP-IP across Windows. Can remove custom IP/DNS settings and interrupt connections. Restart required; no automatic restore.",
        "windows.mmcss.audio-window" => "Requests High MMCSS scheduling for registered Audio and Window Manager threads. May affect other workloads; restart Windows after changing.",
        "gaming.mmcss-priority.boost" => "Requests High MMCSS scheduling for threads registered with the Games task. Other process threads stay unchanged; restart Windows after changing.",
        "storage.optimize-system-drive" => "Runs media-aware Windows optimization on the system drive. May take several minutes; waits for completion. One-time maintenance with no restore setting.",
        "network.tcp.time-wait.30" => "Retains closed TCP connections in TIME_WAIT for 30 seconds. Affects connection reuse, not active-connection ping or UDP traffic. Restart Windows after changing.",
        "network.wifi.power.maximum" => "Requests Maximum Performance for wireless adapters on AC and battery in the current power plan. Can increase battery drain; Ethernet stays unchanged.",
        "network.wifi.uapsd.disable" => "Disables Wi-Fi U-APSD power saving where supported. May improve access-point compatibility while increasing power use; Ethernet stays unchanged.",
        "network.wifi.mimo-power-save.disable" => "Keeps Wi-Fi receive antennas available with No SMPS. Uses more power; requires driver support and may restart the adapter.",
        "network.wifi.prefer-5ghz" => "Prefers 5GHz when available, with fallback to other bands. Range can be shorter than 2.4GHz; Ethernet stays unchanged.",
        "network.wifi.roaming.maximum" => "Sets the highest driver-advertised roaming aggressiveness. Scans for other access points sooner and can increase interruptions.",
        "network.wifi.transmit.maximum" => "Sets the highest driver-advertised Wi-Fi transmit power within regulatory limits. Can increase interference and power use.",
        "network.adapter.rsc.disable" => "Disables IPv4/IPv6 receive coalescing on the active physical adapter. May restart the adapter and reduce throughput; restores both original protocol states.",
        "network.adapter.lso.disable" => "Disables IPv4/IPv6 large send offload v2 on the active physical adapter. May restart the adapter and increase CPU work; restores both original protocol states.",
        "power.network.task-offload.disable" => "Disables global TCP/IP task offloading. Packet processing moves to the CPU; restart Windows or the network adapter to apply.",
        "power.policy.never-sleep-ac" => "Prevents automatic display-off and idle sleep while plugged in, and disables wake timers. Battery power settings stay unchanged.",
        "power.network.transition-tunnels.disable" => "Disables Teredo and ISATAP transition tunnels. Native IPv6 stays enabled; Xbox or enterprise tunnel connections may be affected.",
        "power.network.timestamps.disable" => "Disables global TCP timestamps while leaving receive-window scaling unchanged. Restore returns the original timestamp mode.",
        "windows.cleanup.temp" => "Clears accessible user and Windows temporary files. Locked files and directory links are skipped; deleted files cannot be restored.",
        "windows.cleanup.disk-cleanup" => "Runs Windows Disk Cleanup for temporary files and selected caches. Downloads, Recycle Bin and Windows rollback files stay excluded.",
        "windows.cleanup.shaders" => "Clears known DirectX, NVIDIA and AMD shader caches. Games rebuild shaders on later launches and may temporarily stutter.",
        "windows.cleanup.recycle-bin" => "Permanently empties your Recycle Bin across connected drives. Review it first; deleted contents cannot be restored by Elite.",
        _ => null
    };

    private void UpdateActions()
    {
        _apply.Content = $"Apply selected ({_selected.Count})";
        _restore.Content = "Restore selected";
        _apply.IsEnabled = _selected.Count > 0;
        _restore.IsEnabled = _selected.Any(row => !row.Definition.IsOneTimeAction);
        _recommended.IsEnabled = CurrentTab != null;
        _undoRecommended.IsEnabled = CurrentTab is { } tab
            && GroupsFor(tab).SelectMany(group => group.Rows).Any(_recommendedChanges.Contains);
    }

    private static TextBlock Text(string value, double size, Brush foreground) =>
        new() { Text = value, FontSize = size, Foreground = foreground, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 4) };

    internal static Button MakeButton(string label) =>
        new() { Template = ButtonTemplate, BorderThickness = new Thickness(1), Background = new SolidColorBrush(Color.FromRgb(35, 27, 50)), Foreground = Brushes.White, BorderBrush = new SolidColorBrush(Color.FromRgb(64, 49, 88)), Content = label, Margin = new Thickness(0, 2, 0, 2), Padding = new Thickness(8, 5, 8, 5) };
}


































