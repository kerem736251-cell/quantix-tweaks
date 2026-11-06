using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;

namespace QuantixTweakingUtility.Views;

using Services;

public sealed class NetworkPanel : Border
{
    private readonly TextBlock details = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 8) };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
    private readonly TextBlock restorePoint = new() { TextWrapping = TextWrapping.Wrap, Opacity = .7, FontSize = 11 };
    private readonly WrapPanel buttons = new();
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(15) };
    private Button ethernetButton = null!;
    private bool busy;
    private bool refreshing;
    public string DetailsText => details.Text;
    public bool EthernetActionVisible => ethernetButton.Visibility == Visibility.Visible;

    public NetworkPanel()
    {
        Background = new SolidColorBrush(Color.FromRgb(19, 22, 29));
        BorderBrush = new SolidColorBrush(Color.FromRgb(45, 50, 61));
        BorderThickness = new Thickness(1); CornerRadius = new CornerRadius(10);
        Padding = new Thickness(18); Margin = new Thickness(0, 0, 0, 16);
        var content = new StackPanel();
        content.Children.Add(new TextBlock { Text = "ADAPTIVE NETWORK | " + NetworkWorkflow.Tier.ToUpperInvariant() + " v" + VersionLabels.Version,
            FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromRgb(173, 143, 255)) });
        details.Foreground = status.Foreground = restorePoint.Foreground = Brushes.White;
        content.Children.Add(details);
        AddButton("Optimize Network", async () => await RunAsync("Optimize"));
        AddButton("Restore Defaults", async () => await RunAsync("Restore"));
        AddButton("Refresh", RefreshAsync);
        ethernetButton = AddButton("Ethernet: IPv4/IPv6 only + disable extras", async () => {
            if (MessageBox.Show("Disable all other bindings and supported listed Ethernet driver features on this adapter? File sharing, VPNs and virtual networking may stop working; RSS/offload changes can reduce throughput. The connection may briefly drop. Original settings are saved for Restore Defaults. A Windows restart may be needed.", "Ethernet profile", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
                await RunAsync("EthernetMinimal");
        });
        ethernetButton.Visibility = Visibility.Collapsed;
        content.Children.Add(buttons); content.Children.Add(new ScrollViewer { Content = status, MaxHeight = 150, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }); content.Children.Add(restorePoint);
        Child = content;
        status.Text = NetworkWorkflow.Tier == "Elite"
            ? "Wi-Fi: reduce update sharing + AC wireless performance. Ethernet: reduce update sharing + supported RSS. Battery and DNS preferences are preserved."
            : "Safe profile: reduce Windows Update peer sharing on supported Wi-Fi or Ethernet. Adapter, TCP and DNS preferences are preserved.";
        timer.Tick += async (_, _) => await RefreshAsync();
        Loaded += async (_, _) => { timer.Start(); await RefreshAsync(); };
        Unloaded += (_, _) => timer.Stop();
    }

    private Button AddButton(string title, Func<Task> action)
    {
        var button = new Button { Content = title, Padding = new Thickness(12, 7, 12, 7),
            Margin = new Thickness(0, 0, 8, 0), MinWidth = 105,
            Background = new SolidColorBrush(Color.FromRgb(49, 39, 73)),
            Foreground = Brushes.White, BorderBrush = new SolidColorBrush(Color.FromRgb(96, 75, 139)) };
        button.Click += async (_, _) => { try { await action(); } catch (Exception ex) { status.Text = ex.Message; } };
        buttons.Children.Add(button);
        return button;
    }

    public async Task RefreshAsync()
    {
        restorePoint.Text = SessionRestorePoint.Status;
        if (refreshing || busy) return;
        refreshing = true;
        try
        {
            var result = await NetworkWorkflow.RunAsync("Inspect");
            SetConnectionDetails(result);
        }
        finally { refreshing = false; }
    }

    public void SetConnectionDetails(NetworkWorkflowResult result)
    {
        var n = result.Connection;
        ethernetButton.Visibility = n?.Type == "Ethernet" && n.Physical ? Visibility.Visible : Visibility.Collapsed;
        details.Text = n is null ? result.Message :
            $"Connection Type: {n.Type}    Active Adapter: {n.Adapter}\nLink Speed: {n.LinkSpeed}    DNS: {n.Dns}\nStatus: {n.Status}";
    }

    private async Task RunAsync(string mode)
    {
        if (busy) return;
        busy = true; buttons.IsEnabled = false;
        status.Text = mode == "Restore" ? "Restoring saved original values..." : "Checking adapter capabilities and saving original values...";
        try
        {
            var result = await NetworkWorkflow.RunAsync(mode);
            status.Text = result.Message;
            if (mode == "Restore") status.Text += "\n" + await RestoreLegacyAsync();
        }
        finally { busy = false; buttons.IsEnabled = true; await RefreshAsync(); }
    }

    private static async Task<string> RestoreLegacyAsync()
    {
        // Retain access to pre-upgrade backups; never invent defaults for unrecorded changes.
        try
        {
            var backup = App.Services.GetRequiredService<IRestoreService>();
            var saved = backup.GetRestorableTweakIds();
#if QUANTIX_ELITE
            var ids = App.Services.GetRequiredService<ITweakService>().GetTweaksByCategory(QuantixTweakingUtility.Models.TweakCategory.Network).Where(t => saved.Contains(t.Id)).Select(t => t.Id).ToArray();
#else
            var ids = App.Services.GetRequiredService<IOptimizationService>().GetActions(QuantixTweakingUtility.Models.OptimizationSection.Network).Where(t => saved.Contains(t.Id)).Select(t => t.Id).ToArray();
#endif
            if (ids.Length == 0) return "No legacy network backups.";
            var notes = new System.Collections.Generic.List<string>();
#if QUANTIX_ELITE
            var service = App.Services.GetRequiredService<ITweakService>();
            foreach (string id in ids) {
                var result = await service.RestoreTweakAsync(id);
                notes.Add(result.Message);
            }
#else
            var service = App.Services.GetRequiredService<IOptimizationService>();
            foreach (string id in ids) {
                var result = await service.RestoreAsync(id);
                notes.Add(result.Message);
            }
#endif
            return string.Join("\n", notes);
        }
        catch (Exception ex) { return "Legacy recovery: " + ex.Message; }
    }

    public static Grid Wrap(UIElement existing)
    {
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.Children.Add(new NetworkPanel());
        Grid.SetRow(existing, 1); grid.Children.Add(existing);
        return grid;
    }
}
