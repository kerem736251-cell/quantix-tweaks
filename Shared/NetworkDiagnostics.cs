using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using QuantixTweakingUtility.Views;
using QuantixTweakingUtility.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Generic;

namespace QuantixTweakingUtility.Services;

public static class NetworkDiagnostics
{
    public static async Task<bool> RunVisibleAsync(string[] args)
    {
        int index = Array.IndexOf(args, "--verify-visible-network");
        if (index < 0) return false;
        string path = args[index + 1];
        try {
            var vm = App.Services.GetRequiredService<MainViewModel>();
            var window = App.Services.GetRequiredService<MainWindow>();
            window.Width = 1280; window.Height = 900;
            window.Left = -12000; window.Top = -12000; window.ShowInTaskbar = false;
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Show();
            vm.SelectedNavItem = vm.NavItems.Single(n => n.Key == "network");
            window.UpdateLayout();
            if (FindPanels(window).Count != 0) throw new InvalidOperationException("Removed network panel is still visible.");
#if QUANTIX_ELITE
            var controls=App.Services.GetRequiredService<ITweakService>().GetTweaksByCategory(QuantixTweakingUtility.Models.TweakCategory.Network);
            if (controls.Count == 0 || controls.Any(t => t.RestoreOnly || t.Name.Contains("legacy recovery"))) throw new InvalidOperationException("Original controls still blocked.");
#else
            var controls=App.Services.GetRequiredService<IOptimizationService>().GetActions(QuantixTweakingUtility.Models.OptimizationSection.Network);
            if (controls.Count == 0 || controls.Any(t => t.RestoreOnly || t.Name.Contains("legacy recovery"))) throw new InvalidOperationException("Original controls still blocked.");
#endif
            vm.SelectedNavItem=vm.NavItems.First(n => n.Key != "network"); window.UpdateLayout();
            vm.SelectedNavItem=vm.NavItems.Single(n => n.Key == "network"); window.UpdateLayout();
            if (FindPanels(window).Count != 0) throw new InvalidOperationException("Removed panel reappeared.");
            var bitmap = new RenderTargetBitmap(1280, 900, 96, 96, PixelFormats.Pbgra32); bitmap.Render(window);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var file = File.Create(path)) encoder.Save(file);
            File.WriteAllText(path + ".txt", "PASS: original Network controls enabled; new panel removed; tab navigation passed. Controls: " + controls.Count);
            window.Close(); Application.Current.Shutdown(0);
        } catch(Exception ex) { File.WriteAllText(path + ".error.txt", ex.ToString()); Application.Current.Shutdown(72); }
        return true;
    }

    private static List<NetworkPanel> FindPanels(DependencyObject root)
    {
        var result = new List<NetworkPanel>();
        if (root is NetworkPanel panel) result.Add(panel);
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) result.AddRange(FindPanels(VisualTreeHelper.GetChild(root, i)));
        return result;
    }

    public static async Task<bool> RunAsync(string[] args)
    {
        int index = Array.IndexOf(args, "--verify-network");
        if (index < 0) return false;
        try
        {
            string path = Path.GetFullPath(args[index + 1]);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            int calls = 0;
            for (int i = 0; i < 30; i++)
                SessionRestorePoint.StartOnce(create: () => {
                    Interlocked.Increment(ref calls);
                    throw new UnauthorizedAccessException("Injected missing-rights failure.");
                });
            await SessionRestorePoint.Completion!;
            if (calls != 1 || !SessionRestorePoint.Status.Contains("unavailable"))
                throw new InvalidOperationException("Session restore-point guard failed.");
            var result = await NetworkWorkflow.RunAsync("Inspect");
            if (!result.Success || result.Connection == null) throw new InvalidOperationException(result.Message);
            var panel = new NetworkPanel();
            panel.SetConnectionDetails(result);
            var window = new Window { Content = panel, Width = 1100, Height = 330,
                Left = -12000, Top = -12000, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual,
                Background = new SolidColorBrush(Color.FromRgb(12, 14, 19)) };
            window.Show();
            await panel.RefreshAsync();
            // Loaded also requests a refresh; allow its asynchronous operation to complete.
            await Task.Delay(1500);
            window.UpdateLayout();
            var bitmap = new RenderTargetBitmap(1100, 330, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(window);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var file = File.Create(path)) encoder.Save(file);
            window.Close();
            File.WriteAllText(path + ".json", System.Text.Json.JsonSerializer.Serialize(result,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            Application.Current.Shutdown(0);
        }
        catch (Exception ex) {
            if (index + 1 < args.Length) File.WriteAllText(args[index + 1] + ".error.txt", ex.ToString());
            Application.Current.Shutdown(70);
        }
        return true;
    }
}
