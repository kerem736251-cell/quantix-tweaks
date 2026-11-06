using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Extensions.DependencyInjection;
using QuantixTweakingUtility.Views;
using QuantixTweakingUtility.ViewModels;

namespace QuantixTweakingUtility.Services;

public static class CleanupDiagnostics
{
    private static bool HasEntry(DependencyObject root) {
        if (root is AutomaticCleanupPanel) return true;
        for (int i=0; i<VisualTreeHelper.GetChildrenCount(root); i++) if(HasEntry(VisualTreeHelper.GetChild(root,i))) return true;
        return false;
    }
    private static AutomaticCleanupPanel? FindPanel(DependencyObject root) {
        if(root is AutomaticCleanupPanel panel)return panel;
        for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)if(FindPanel(VisualTreeHelper.GetChild(root,i)) is AutomaticCleanupPanel found)return found;
        return null;
    }
    private static void Render(Window window,string path) {
        window.UpdateLayout();
        var bitmap=new RenderTargetBitmap((int)window.ActualWidth,(int)window.ActualHeight,96,96,PixelFormats.Pbgra32); bitmap.Render(window);
        var encoder=new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file=File.Create(path); encoder.Save(file);
    }
    public static async Task<bool> RunAsync(string[] args) {
        int index=Array.IndexOf(args,"--verify-cleanup"); if(index<0)return false;
        string path=args[index+1];
        try {
            var vm=App.Services.GetRequiredService<MainViewModel>();
            var main=App.Services.GetRequiredService<MainWindow>();
            main.WindowStartupLocation=WindowStartupLocation.Manual; main.Left=-12000;main.Top=-12000;main.ShowInTaskbar=false;main.Width=1280;main.Height=900;main.Show();
            vm.SelectedNavItem=vm.NavItems.Single(n=>n.Key=="cleanup");main.UpdateLayout();
            if(!HasEntry(main))throw new InvalidOperationException("Cleanup entry missing from actual sidebar view.");
            if(AutomaticCleanupPanel.Targets.Any(id=>!GamingCleanupWindow.Catalog.ContainsKey(id)) || AutomaticCleanupPanel.Targets.Contains("WSearch") || AutomaticCleanupPanel.Targets.Contains("Microsoft.YourPhone")) throw new InvalidOperationException("Automatic preset includes unsupported or data-sensitive targets."); Render(main,path+".navigation.png");
            var automatic=FindPanel(main)??throw new Exception("Automatic cleanup panel missing");automatic.PreviewOptimization(true);main.UpdateLayout();
            if(!automatic.OptimizationVisible || automatic.ShownProgress!=20 || (SystemParameters.ClientAreaAnimation && !automatic.AnimationRunning))throw new Exception("Optimization effect or real-step progress missing");Render(main,path+".optimization.png");automatic.PreviewOptimization(false);if(automatic.OptimizationVisible || automatic.AnimationRunning)throw new Exception("Optimization clocks were not stopped");
            var window=new GamingCleanupWindow {WindowStartupLocation=WindowStartupLocation.Manual,Left=-12000,Top=-12000,ShowInTaskbar=false};window.Show();
            var scan=window.VerifyReadOnlyScanAsync();
            if(!window.IsWorking || !window.LoadingVisible)throw new InvalidOperationException("Read-only scan did not show loading state.");
            Render(window,path+".loading.png");
            await scan;
            if(window.IsWorking || window.LoadingVisible || window.ReportText.Contains("could not complete"))throw new InvalidOperationException("Scan failed or left controls busy: "+window.ReportText);
            if(GamingCleanupWindow.Catalog.ContainsKey("FrameServer") || GamingCleanupWindow.Catalog.ContainsKey("Microsoft.GamingServices"))throw new InvalidOperationException("Protected dependency exposed for cleanup.");
            Render(window,path+".scan.png");
            File.WriteAllText(path+".txt","PASS: actual Cleanup navigation entry, animated optimization preview with real-step progress and stopped clocks, loading state during live read-only scan, completed scan, protected target exclusions. No apps/services changed.");
            window.Close(); main.Close(); Application.Current.Shutdown(0);
        }catch(Exception ex){File.WriteAllText(path+".error.txt",ex.ToString());Application.Current.Shutdown(74);}
        return true;
    }
}

