using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Collections.Generic;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using QuantixTweakingUtility.ViewModels;

namespace QuantixTweakingUtility.Services;

internal static class WorkspaceDiagnostics
{
    public static async Task<bool> RunAsync(string[] args)
    {
        int option = Array.IndexOf(args, "--verify-workspace");
        if (option < 0) return false;
        string folder = Path.GetFullPath(args[option + 1]);
        Directory.CreateDirectory(folder);
        try
        {
            RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;
            var main = App.Services.GetRequiredService<MainWindow>();
            var model = App.Services.GetRequiredService<MainViewModel>();
            main.WindowStartupLocation = WindowStartupLocation.Manual;
            main.Left = -12000; main.Top = -12000; main.ShowInTaskbar = false;
            main.Width = 1050; main.Height = 660;
            main.Show();
            if (!model.NavItems.Select(item=>item.Key).SequenceEqual(new[] { "dashboard", "basic", "processes", "network", "debloat", "gpu", "advanced" }))
                throw new InvalidOperationException("Only the seven primary navigation pages should remain.");
            if (Descendants<Button>(main).Any(button=>Equals(button.Content,"···")
                || System.Windows.Automation.AutomationProperties.GetName(button)=="More Elite controls"))
                throw new InvalidOperationException("The removed extra-pages menu is still present.");
            foreach (var key in new[] { "dashboard", "basic", "processes", "network", "debloat", "gpu", "advanced" })
            {
                model.SelectedNavItem = model.NavItems.Single(item => item.Key == key);
                await Descendants<EliteWorkspace>(main).Single().WaitForCurrentPageAsync().WaitAsync(TimeSpan.FromSeconds(60));
                await Task.Delay(key == "dashboard" ? 1800 : 700);
                main.UpdateLayout();
                await main.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
                File.WriteAllLines(Path.Combine(folder, key + ".controls.txt"), Buttons(main));
                File.WriteAllLines(Path.Combine(folder, key + ".states.tsv"), Descendants<EliteWorkspace>(main).Single().CurrentStateReport());
                var bitmap = new RenderTargetBitmap(1050, 660, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(main);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var file = File.Create(Path.Combine(folder, key + ".png"));
                encoder.Save(file);
                if (key == "dashboard")
                {
                    var regionMenu=Descendants<QuantixTweakingUtility.Views.FortniteLatencyPanel>(main).Single().RegionMenu;
                    if(regionMenu.Items.Count!=FortniteLatencyService.Endpoints.Count || regionMenu.Template==null
                        || regionMenu.ItemContainerStyle==null || !regionMenu.Resources.Contains(typeof(System.Windows.Controls.Primitives.ScrollBar)))
                        throw new InvalidOperationException("Region menu is missing its custom dark templates or endpoints.");
                    regionMenu.Width=300;regionMenu.Height=320;
                    regionMenu.ApplyTemplate();regionMenu.Measure(new Size(300,320));regionMenu.Arrange(new Rect(0,0,300,320));regionMenu.UpdateLayout();
                    var regionBitmap=new RenderTargetBitmap(300,320,96,96,PixelFormats.Pbgra32);regionBitmap.Render(regionMenu);
                    var regionEncoder=new PngBitmapEncoder();regionEncoder.Frames.Add(BitmapFrame.Create(regionBitmap));
                    using(var regionFile=File.Create(Path.Combine(folder,"ping-region-menu.png")))regionEncoder.Save(regionFile);
                    if(Descendants<Border>(regionMenu).Any(border=>border.Background is SolidColorBrush brush && brush.Color==Colors.White))
                        throw new InvalidOperationException("A white background remains in the ping region menu.");
                    regionMenu.Width=double.NaN;regionMenu.Height=double.NaN;
                    var chart = Descendants<EliteCpuChart>(main).Single();
                    double? memorySample = null;
                    void OnSample(EliteCpuChart.Metric metric, double? value) { if (metric == EliteCpuChart.Metric.Ram) memorySample = value; }
                    chart.SampleUpdated += OnSample;
                    Descendants<Button>(main).Single(button => Equals(button.Content,"RAM")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    if (!memorySample.HasValue || memorySample < 0 || memorySample > 1)
                        throw new InvalidOperationException("RAM chart did not produce a valid native sample.");
                    var status = Descendants<TextBlock>(main).Single(block => block.ToolTip is string tip && tip.StartsWith("RAM utilization:"));
                    if(status.Text != MetricLoadStatus.Describe(memorySample))
                        throw new InvalidOperationException("Dashboard status did not follow the selected native metric.");
                    main.UpdateLayout();
                    var memoryBitmap = new RenderTargetBitmap(1050,660,96,96,PixelFormats.Pbgra32);memoryBitmap.Render(main);
                    var memoryEncoder = new PngBitmapEncoder();memoryEncoder.Frames.Add(BitmapFrame.Create(memoryBitmap));
                    using(var memoryFile=File.Create(Path.Combine(folder,"dashboard-ram.png")))memoryEncoder.Save(memoryFile);
                    chart.SampleUpdated -= OnSample;
                    Descendants<Button>(main).Single(button => Equals(button.Content,"CPU")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                }
                if (key == "debloat")
                {
                    if(Descendants<CheckBox>(main).Count(box=>box.Content is TextBlock label && label.Text=="Browser Updater Services Off")!=1)
                        throw new InvalidOperationException("Browser updater card is missing or duplicated.");
                    var scroll=Descendants<ScrollViewer>(main).OrderByDescending(view=>view.ScrollableHeight).First();
                    scroll.ScrollToBottom();main.UpdateLayout();
                    var browserBitmap=new RenderTargetBitmap(1050,660,96,96,PixelFormats.Pbgra32);browserBitmap.Render(main);
                    var browserEncoder=new PngBitmapEncoder();browserEncoder.Frames.Add(BitmapFrame.Create(browserBitmap));
                    using(var browserFile=File.Create(Path.Combine(folder,"debloat-browser.png")))browserEncoder.Save(browserFile);
                    scroll.ScrollToTop();
                }
                if(key=="gpu")
                {
                    var telemetry=Descendants<CheckBox>(main).Single(box=>box.Content is TextBlock label && label.Text=="Windows Telemetry Tasks Off");
                    var scroll=Descendants<ScrollViewer>(main).OrderByDescending(view=>view.ScrollableHeight).First();
                    scroll.ScrollToVerticalOffset(telemetry.TranslatePoint(new Point(),(UIElement)scroll.Content).Y-40);main.UpdateLayout();
                    var taskBitmap=new RenderTargetBitmap(1050,660,96,96,PixelFormats.Pbgra32);taskBitmap.Render(main);
                    var taskEncoder=new PngBitmapEncoder();taskEncoder.Frames.Add(BitmapFrame.Create(taskBitmap));
                    using(var taskFile=File.Create(Path.Combine(folder,"gpu-telemetry.png")))taskEncoder.Save(taskFile);
                    scroll.ScrollToTop();
                }
                if (key == "advanced")
                {
                    if(Descendants<CheckBox>(main).Count(box=>box.Content is TextBlock label && label.Text=="Discord Hardware Acceleration Off")!=1
                        || !Descendants<TextBlock>(main).Any(block=>block.Text=="Game Tweaks"))
                        throw new InvalidOperationException("Discord acceleration card or Game Tweaks section missing.");
                    foreach(string name in new[]{"Data Queue Size","Mouse Data Queue Size"})
                        if(Descendants<CheckBox>(main).Count(box=>box.Content is TextBlock label && label.Text==name)!=1)
                            throw new InvalidOperationException("Missing or duplicate KBM queue card: "+name);
                    var headings=Descendants<TextBlock>(main).Select(block=>block.Text).ToArray();
                    if(Array.IndexOf(headings,"KBM Tweaks")<0 || Array.IndexOf(headings,"Additional Tweaks")<Array.IndexOf(headings,"KBM Tweaks"))
                        throw new InvalidOperationException("KBM must precede Additional Tweaks as in the reference.");
                }
                if (key == "network")
                {
                    if(Descendants<CheckBox>(main).Count(box=>box.Content is TextBlock label && label.Text=="NIC MSI Mode")!=1)
                        throw new InvalidOperationException("NIC MSI card is missing or duplicated.");
                    if (Descendants<CheckBox>(main).Count(box => box.Content is TextBlock label && label.Text == "TcpTimedWaitDelay") != 1)
                        throw new InvalidOperationException("The TCP TIME_WAIT card is missing or duplicated.");
                    var resetCard=Descendants<CheckBox>(main).Single(box=>box.Content is TextBlock label && label.Text=="Reset all network settings");
                    if(!resetCard.IsEnabled)throw new InvalidOperationException("Installed network reset tools should be selectable.");
                    resetCard.IsChecked=true;
                    if(!Descendants<Button>(main).Any(button=>Equals(button.Content,"Apply selected (1)") && button.IsEnabled)
                        || !Descendants<Button>(main).Any(button=>Equals(button.Content,"Restore selected") && !button.IsEnabled))
                        throw new InvalidOperationException("Network reset must enable Apply and disable Restore.");
                    main.UpdateLayout();
                    var resetBitmap=new RenderTargetBitmap(1050,660,96,96,PixelFormats.Pbgra32);resetBitmap.Render(main);
                    var resetEncoder=new PngBitmapEncoder();resetEncoder.Frames.Add(BitmapFrame.Create(resetBitmap));
                    using(var resetFile=File.Create(Path.Combine(folder,"network-reset-selected.png")))resetEncoder.Save(resetFile);
                    resetCard.IsChecked=false;
                    var scroll = Descendants<ScrollViewer>(main).OrderByDescending(view => view.ScrollableHeight).First();
                    var heading = Descendants<TextBlock>(main).Single(block => block.Text == "Adapter Tweaks");
                    scroll.ScrollToVerticalOffset(heading.TranslatePoint(new Point(), (UIElement)scroll.Content).Y - 8);
                    main.UpdateLayout();
                    await main.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
                    foreach (string name in new[] { "NIC Coalescing Off (RSC)", "NIC Large Send Offload Off" })
                        if (Descendants<CheckBox>(main).Count(box => box.Content is TextBlock label && label.Text == name) != 1)
                            throw new InvalidOperationException("Missing or duplicate offload card: " + name);
                    var adapterBitmap = new RenderTargetBitmap(1050, 660, 96, 96, PixelFormats.Pbgra32);
                    adapterBitmap.Render(main);
                    var adapterEncoder = new PngBitmapEncoder();
                    adapterEncoder.Frames.Add(BitmapFrame.Create(adapterBitmap));
                    using (var adapterFile = File.Create(Path.Combine(folder, "network-adapters.png"))) adapterEncoder.Save(adapterFile);
                    var wifiHeading = Descendants<TextBlock>(main).Single(block => block.Text == "Wifi Tweaks");
                    scroll.ScrollToVerticalOffset(wifiHeading.TranslatePoint(new Point(), (UIElement)scroll.Content).Y - 8);
                    main.UpdateLayout();
                    await main.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
                    foreach (string name in new[] { "Max Wireless Performance", "Disable U-APSD Power Save", "Disable MIMO Power Save", "Prefer 5GHz Band", "Max Roaming Aggressiveness", "Max Transmit Power" })
                        if (Descendants<CheckBox>(main).Count(box => box.Content is TextBlock label && label.Text == name) != 1)
                            throw new InvalidOperationException("Missing or duplicate Wi-Fi card: " + name);
                    var wifiBitmap = new RenderTargetBitmap(1050, 660, 96, 96, PixelFormats.Pbgra32);
                    wifiBitmap.Render(main);
                    var wifiEncoder = new PngBitmapEncoder();
                    wifiEncoder.Frames.Add(BitmapFrame.Create(wifiBitmap));
                    using (var wifiFile = File.Create(Path.Combine(folder, "network-wifi.png"))) wifiEncoder.Save(wifiFile);
                    scroll.ScrollToTop();
                }
                if (key == "processes")
                {
                    var scroll = Descendants<ScrollViewer>(main).OrderByDescending(view => view.ScrollableHeight).First();
                    scroll.ScrollToBottom();
                    main.UpdateLayout();
                    await main.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
                    var lower = new RenderTargetBitmap(1050, 660, 96, 96, PixelFormats.Pbgra32);
                    lower.Render(main);
                    var lowerEncoder = new PngBitmapEncoder();
                    lowerEncoder.Frames.Add(BitmapFrame.Create(lower));
                    using (var lowerFile = File.Create(Path.Combine(folder, "processes-cleanup.png"))) lowerEncoder.Save(lowerFile);
                    var actions = Descendants<CheckBox>(main).Where(box => box.Content is TextBlock label
                        && label.Text is "Kill Background Browsers" or "Kill Bloat Processes Now").ToArray();
                    if (actions.Length != 2 || actions.Any(box => !box.IsEnabled))
                        throw new InvalidOperationException("Both one-time process cleanup cards must be selectable.");
                    foreach (var box in actions) box.IsChecked = true;
                    if (!Descendants<Button>(main).Any(button => Equals(button.Content, "Apply selected (2)") && button.IsEnabled)
                        || !Descendants<Button>(main).Any(button => Equals(button.Content, "Restore selected") && !button.IsEnabled))
                        throw new InvalidOperationException("One-time cleanup selection must enable Apply and disable Restore.");
                    main.UpdateLayout();
                    var actionSelection = new RenderTargetBitmap(1050, 660, 96, 96, PixelFormats.Pbgra32);
                    actionSelection.Render(main);
                    var actionEncoder = new PngBitmapEncoder();
                    actionEncoder.Frames.Add(BitmapFrame.Create(actionSelection));
                    using (var actionFile = File.Create(Path.Combine(folder, "processes-cleanup-selected.png"))) actionEncoder.Save(actionFile);
                    foreach (var box in actions) box.IsChecked = false;
                    scroll.ScrollToTop();
                }
                if (key == "basic")
                {
                    foreach(string name in new[]{"Disable UAC","Disable GPU Timeout Detection (Tdr)"})
                        if(Descendants<CheckBox>(main).Count(box=>box.Content is TextBlock label && label.Text==name)!=1)
                            throw new InvalidOperationException("Missing or duplicate protection-control card: "+name);
                    foreach(string name in new[]{"USB Controller MSI Mode","NVMe MSI Mode"})
                        if(Descendants<CheckBox>(main).Count(box=>box.Content is TextBlock label && label.Text==name)!=1)
                            throw new InvalidOperationException("Missing or duplicate PCI interrupt card: "+name);
                    var selected = Descendants<CheckBox>(main).Where(box => box.IsEnabled).Take(4).ToArray();
                    if (selected.Length != 4) throw new InvalidOperationException("Four selectable Windows cards were not available for the visual review.");
                    foreach (var box in selected) box.IsChecked = true;
                    main.UpdateLayout();
                    await main.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
                    if (!Descendants<Button>(main).Any(button => Equals(button.Content, "Apply selected (4)") && button.IsEnabled))
                        throw new InvalidOperationException("Selected card count or apply availability is incorrect.");
                    var picked = new RenderTargetBitmap(1050, 660, 96, 96, PixelFormats.Pbgra32);
                    picked.Render(main);
                    var pickedEncoder = new PngBitmapEncoder();
                    pickedEncoder.Frames.Add(BitmapFrame.Create(picked));
                    using (var selectedFile = File.Create(Path.Combine(folder, "basic-selected.png"))) pickedEncoder.Save(selectedFile);
                    foreach (var box in selected) box.IsChecked = false;
                    var usbHeading=Descendants<TextBlock>(main).Single(block=>block.Text=="USB Tweaks");
                    var usbScroll=Descendants<ScrollViewer>(main).OrderByDescending(view=>view.ScrollableHeight).First();
                    usbScroll.ScrollToVerticalOffset(usbHeading.TranslatePoint(new Point(),(UIElement)usbScroll.Content).Y-8);main.UpdateLayout();
                    var usbBitmap=new RenderTargetBitmap(1050,660,96,96,PixelFormats.Pbgra32);usbBitmap.Render(main);
                    var usbEncoder=new PngBitmapEncoder();usbEncoder.Frames.Add(BitmapFrame.Create(usbBitmap));
                    using(var usbFile=File.Create(Path.Combine(folder,"basic-usb.png")))usbEncoder.Save(usbFile);
                    foreach(string name in new[]{"On","Disable Dynamic Tick","x2apic Enable"})
                        if(Descendants<CheckBox>(main).Count(box=>box.Content is TextBlock label && label.Text==name)!=1)
                            throw new InvalidOperationException("Missing or duplicate BCD card: "+name);
                    var bootHeading=Descendants<TextBlock>(main).Single(block=>block.Text=="BCD-Edit");
                    usbScroll.ScrollToVerticalOffset(bootHeading.TranslatePoint(new Point(),(UIElement)usbScroll.Content).Y-8);main.UpdateLayout();
                    var bootBitmap=new RenderTargetBitmap(1050,660,96,96,PixelFormats.Pbgra32);bootBitmap.Render(main);
                    var bootEncoder=new PngBitmapEncoder();bootEncoder.Frames.Add(BitmapFrame.Create(bootBitmap));
                    using(var bootFile=File.Create(Path.Combine(folder,"basic-bcd.png")))bootEncoder.Save(bootFile);
                    var performance = Descendants<TextBlock>(main).Single(block => block.Text == "Performance Tweaks");
                    var performanceScroll = Descendants<ScrollViewer>(main).OrderByDescending(view => view.ScrollableHeight).First();
                    performanceScroll.ScrollToVerticalOffset(performance.TranslatePoint(new Point(), (UIElement)performanceScroll.Content).Y - 8);
                    main.UpdateLayout();
                    await main.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
                    var performanceBitmap = new RenderTargetBitmap(1050, 660, 96, 96, PixelFormats.Pbgra32);
                    performanceBitmap.Render(main);
                    var performanceEncoder = new PngBitmapEncoder();
                    performanceEncoder.Frames.Add(BitmapFrame.Create(performanceBitmap));
                    using (var performanceFile = File.Create(Path.Combine(folder, "basic-performance.png"))) performanceEncoder.Save(performanceFile);
                    var prefetchCard = Descendants<CheckBox>(main).Single(box => box.Content is TextBlock label && label.Text == "Prefetch Tuning");
                    performanceScroll.ScrollToVerticalOffset(prefetchCard.TranslatePoint(new Point(), (UIElement)performanceScroll.Content).Y - 260);
                    main.UpdateLayout();
                    await main.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
                    var prefetchBitmap = new RenderTargetBitmap(1050, 660, 96, 96, PixelFormats.Pbgra32);
                    prefetchBitmap.Render(main);
                    var prefetchEncoder = new PngBitmapEncoder();
                    prefetchEncoder.Frames.Add(BitmapFrame.Create(prefetchBitmap));
                    using (var prefetchFile = File.Create(Path.Combine(folder, "basic-prefetch.png"))) prefetchEncoder.Save(prefetchFile);
                    var lastPower = Descendants<CheckBox>(main).Single(box => box.Content is TextBlock label && label.Text == "TCP Timestamps Off");
                    FrameworkElement powerCard = lastPower;
                    while (powerCard is not Border && powerCard.Parent is FrameworkElement parent) powerCard = parent;
                    powerCard.BringIntoView();
                    main.UpdateLayout();
                    await main.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
                    var networkPower = new RenderTargetBitmap(1050, 660, 96, 96, PixelFormats.Pbgra32);
                    networkPower.Render(main);
                    var networkPowerEncoder = new PngBitmapEncoder();
                    networkPowerEncoder.Frames.Add(BitmapFrame.Create(networkPower));
                    using (var powerFile = File.Create(Path.Combine(folder, "basic-power-network.png"))) networkPowerEncoder.Save(powerFile);
                    var cleanerHeading = Descendants<TextBlock>(main).Single(block => block.Text == "Cleaner");
                    performanceScroll.ScrollToVerticalOffset(cleanerHeading.TranslatePoint(new Point(),(UIElement)performanceScroll.Content).Y-8);
                    main.UpdateLayout();
                    var cleanupCards = Descendants<CheckBox>(main).Where(box=>box.Content is TextBlock label
                        && label.Text is "Clean temp all files" or "Windows Cleaner" or "Clear Shader Caches" or "Wipe Recycle Bin").ToArray();
                    if(cleanupCards.Length!=4 || cleanupCards.Any(box=>!box.IsEnabled))throw new InvalidOperationException("All four installed cleanup actions should be selectable.");
                    foreach(var box in cleanupCards)box.IsChecked=true;
                    if(!Descendants<Button>(main).Any(button=>Equals(button.Content,"Apply selected (4)") && button.IsEnabled)
                        || !Descendants<Button>(main).Any(button=>Equals(button.Content,"Restore selected") && !button.IsEnabled))
                        throw new InvalidOperationException("Cleanup one-time actions must enable Apply and disable Restore.");
                    main.UpdateLayout();
                    var cleanerBitmap = new RenderTargetBitmap(1050,660,96,96,PixelFormats.Pbgra32);
                    cleanerBitmap.Render(main);
                    var cleanerEncoder = new PngBitmapEncoder();cleanerEncoder.Frames.Add(BitmapFrame.Create(cleanerBitmap));
                    using(var cleanerFile=File.Create(Path.Combine(folder,"basic-cleaner-selected.png")))cleanerEncoder.Save(cleanerFile);
                    foreach(var box in cleanupCards)box.IsChecked=false;
                    var extraHeading=Descendants<TextBlock>(main).Single(block=>block.Text=="Extra Tweaks");
                    performanceScroll.ScrollToVerticalOffset(extraHeading.TranslatePoint(new Point(),(UIElement)performanceScroll.Content).Y-8);
                    main.UpdateLayout();
                    var extraBitmap=new RenderTargetBitmap(1050,660,96,96,PixelFormats.Pbgra32);extraBitmap.Render(main);
                    var extraEncoder=new PngBitmapEncoder();extraEncoder.Frames.Add(BitmapFrame.Create(extraBitmap));
                    using(var extraFile=File.Create(Path.Combine(folder,"basic-extra-storage.png")))extraEncoder.Save(extraFile);
                    var storageHeading=Descendants<TextBlock>(main).Single(block=>block.Text=="Storage Tweaks");
                    performanceScroll.ScrollToVerticalOffset(storageHeading.TranslatePoint(new Point(),(UIElement)performanceScroll.Content).Y-8);
                    main.UpdateLayout();
                    var optimizeCard=Descendants<CheckBox>(main).Single(box=>box.Content is TextBlock label && label.Text=="Optimize Drive");
                    if(!optimizeCard.IsEnabled)throw new InvalidOperationException("Installed drive optimizer should be selectable.");
                    optimizeCard.IsChecked=true;
                    if(!Descendants<Button>(main).Any(button=>Equals(button.Content,"Apply selected (1)") && button.IsEnabled)
                        || !Descendants<Button>(main).Any(button=>Equals(button.Content,"Restore selected") && !button.IsEnabled))
                        throw new InvalidOperationException("Drive optimization must enable Apply and disable Restore.");
                    main.UpdateLayout();
                    var storageBitmap=new RenderTargetBitmap(1050,660,96,96,PixelFormats.Pbgra32);storageBitmap.Render(main);
                    var storageEncoder=new PngBitmapEncoder();storageEncoder.Frames.Add(BitmapFrame.Create(storageBitmap));
                    using(var storageFile=File.Create(Path.Combine(folder,"basic-storage-selected.png")))storageEncoder.Save(storageFile);
                    optimizeCard.IsChecked=false;
                    var repairScroll = Descendants<ScrollViewer>(main).OrderByDescending(view => view.ScrollableHeight).First();
                    repairScroll.ScrollToBottom();
                    main.UpdateLayout();
                    var repairCards = Descendants<CheckBox>(main).Where(box => box.Content is TextBlock label && label.Text is "SFC /scannow" or "Restore Health").ToArray();
                    if (repairCards.Length != 2 || repairCards.Any(box => !box.IsEnabled))
                        throw new InvalidOperationException("The two Windows repair cards are not selectable.");
                    foreach (var box in repairCards) box.IsChecked = true;
                    if (!Descendants<Button>(main).Any(button => Equals(button.Content, "Apply selected (2)") && button.IsEnabled)
                        || !Descendants<Button>(main).Any(button => Equals(button.Content, "Restore selected") && !button.IsEnabled))
                        throw new InvalidOperationException("Repair actions must enable Apply and disable Restore.");
                    main.UpdateLayout();
                    var repairs = new RenderTargetBitmap(1050, 660, 96, 96, PixelFormats.Pbgra32);
                    repairs.Render(main);
                    var repairEncoder = new PngBitmapEncoder();
                    repairEncoder.Frames.Add(BitmapFrame.Create(repairs));
                    using (var repairFile = File.Create(Path.Combine(folder, "basic-repairs.png"))) repairEncoder.Save(repairFile);
                    foreach (var box in repairCards) box.IsChecked = false;
                    repairScroll.ScrollToTop();
                }
            }
            File.WriteAllText(Path.Combine(folder, "result.txt"), "PASS: all seven primary pages rendered at the production window size. No tweaks applied.");
            main.Close();
            Application.Current.Shutdown(0);
        }
        catch (Exception exception)
        {
            File.WriteAllText(Path.Combine(folder, "error.txt"), exception.ToString());
            Application.Current.Shutdown(77);
        }
        return true;
    }

    private static IEnumerable<string> Buttons(DependencyObject node)
    {
        if (node is Button button && button.Content is string label)
            yield return $"{label}: {button.Visibility}, visible={button.IsVisible}, size={button.ActualWidth:0}x{button.ActualHeight:0}, position={button.TranslatePoint(new Point(), Window.GetWindow(button))}, opacity={button.Opacity}";
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(node); index++)
            foreach (var row in Buttons(VisualTreeHelper.GetChild(node, index))) yield return row;
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject node) where T : DependencyObject
    {
        if (node is T match) yield return match;
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(node); index++)
            foreach (var child in Descendants<T>(VisualTreeHelper.GetChild(node, index))) yield return child;
    }
}
