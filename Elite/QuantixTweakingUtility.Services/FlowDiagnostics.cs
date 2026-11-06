using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using QuantixLicensing;
using QuantixTweakingUtility.ViewModels;
using QuantixTweakingUtility.Models;
using System.Collections.Generic;
using System.Reflection;

namespace QuantixTweakingUtility.Services;

internal static class FlowDiagnostics
{
    public static async Task<bool> RunAsync(string[] args)
    {
        int option = Array.IndexOf(args, "--verify-flows");
        if (option < 0) return false;
        string folder = Path.GetFullPath(args[option + 1]);
        Directory.CreateDirectory(folder);
        int checks = 0;
        void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); checks++; }
        try
        {
            RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;
            var main = App.Services.GetRequiredService<MainWindow>();
            var model = App.Services.GetRequiredService<MainViewModel>();
            main.WindowStartupLocation = WindowStartupLocation.Manual;
            main.Left = -12000; main.Top = -12000; main.ShowInTaskbar = false; main.ShowActivated = false;
            main.Width = 1050; main.Height = 660;
            Application.Current.MainWindow = main;
            main.Show();
            var license = new ReviewLicense();
            var activation = new LicenseViewModel(license);
            var window = new LicenseWindow(activation)
            {
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -12000, Top = -12000, ShowInTaskbar = false, ShowActivated = false
            };
            window.Show();
            await Export(window, Path.Combine(folder, "activation.png"));
            Check(activation.LicenseKey.Length == 0, "Activation did not start with an empty isolated key.");
            Check(!activation.HasError, "Initial activation prompt was incorrectly presented as an error.");
            activation.LicenseKey = "VISUAL-REVIEW-INVALID";
            await activation.ActivateCommand.ExecuteAsync(null);
            Check(activation.HasError && license.Activations == 1, "Invalid activation state did not reach the UI.");
            await Export(window, Path.Combine(folder, "activation-invalid.png"));
            window.Close();

            var clock = Stopwatch.StartNew();
            Window? splash = null;
            await App.ShowStartupAsync(async preview =>
            {
                splash = preview;
                await Export(preview, Path.Combine(folder, "startup.png"));
                Check(preview.Width == 1050 && preview.Height == 660, "Startup does not match the main window size.");
            }, hiddenPreview: true);
            Check(clock.Elapsed >= TimeSpan.FromMilliseconds(2900), "Startup did not execute the production three-second sequence.");
            Check(splash != null && !splash.IsVisible, "Startup window remained open.");

            model.SelectedNavItem = model.NavItems.Single(item => item.Key == "basic");
            var root = (Grid)main.Content;
            var workspace = root.Children[0];
            Action<int, string>? report = null;
            clock.Restart();
            await App.ShowProgressAsync("Applying Tweaks", "4 selected", async () =>
            {
                Check(root.Children.Count == 2 && !workspace.IsEnabled, "Apply overlay did not block the workspace.");
                report!(1, "Applying review step — no Windows changes");
                await Export(main, Path.Combine(folder, "apply.png"));
                report(4, "4 of 4 processed");
            }, main, 4, update => report = update);
            Check(clock.Elapsed >= TimeSpan.FromMilliseconds(1900), "Apply panel closed before its two-second minimum.");
            Check(root.Children.Count == 1 && workspace.IsEnabled, "Apply overlay failed to restore the workspace.");

            bool rejected = false;
            try
            {
                await App.ShowProgressAsync("Applying Tweaks", "Failure review", () => Task.FromException(new IOException("Injected review failure")), main);
            }
            catch (IOException) { rejected = true; }
            Check(rejected && root.Children.Count == 1 && workspace.IsEnabled, "Failed operation left an overlay or disabled workspace.");
            workspace.IsEnabled = false;
            await App.ShowProgressAsync("Restoring Tweaks", "State preservation review", () => Task.CompletedTask, main);
            Check(!workspace.IsEnabled && root.Children.Count == 1, "Overlay changed its owner's previous enabled state.");
            workspace.IsEnabled = true;
            var fake = new ReviewPowerBatch();
            var planRow = new TweakRowViewModel(fake.Definition(BatchOperationOrder.PowerPlan), fake);
            var usbRow = new TweakRowViewModel(fake.Definition("perf.usb-selective-suspend"), fake);
            await Task.WhenAll(planRow.Initialization, usbRow.Initialization);
            var runBatch = typeof(EliteWorkspace).GetMethod("RunAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var batchWorkspace = ((Panel)workspace).Children.OfType<EliteWorkspace>().Single();
            async Task ReviewBatch(bool apply, params TweakRowViewModel[] rows) => await (Task)runBatch.Invoke(batchWorkspace, new object[] { apply, rows })!;
            await ReviewBatch(true, usbRow, planRow);
            Check(fake.Calls.SequenceEqual(new[] { "apply:" + BatchOperationOrder.PowerPlan, "apply:perf.usb-selective-suspend" }), "The production batch did not activate the selected plan before its USB settings.");
            fake.Calls.Clear();
            await ReviewBatch(false, planRow, usbRow);
            Check(fake.Calls.SequenceEqual(new[] { "restore:perf.usb-selective-suspend", "restore:" + BatchOperationOrder.PowerPlan }), "The production batch did not restore plan-specific settings before returning to the previous plan.");
            fake.Calls.Clear(); fake.FailPlan = true;
            await ReviewBatch(true, usbRow, planRow);
            Check(fake.Calls.SequenceEqual(new[] { "apply:" + BatchOperationOrder.PowerPlan }) && usbRow.StatusText.StartsWith("Skipped"), "Failed plan activation did not skip dependent settings.");
            fake.Calls.Clear(); fake.FailPlan = false; fake.FailUsbRestore = true;
            foreach (string id in fake.Applied.Keys.ToArray()) { fake.Applied[id] = true; fake.Saved[id] = true; }
            await ReviewBatch(false, planRow, usbRow);
            Check(fake.Calls.SequenceEqual(new[] { "restore:perf.usb-selective-suspend" }) && fake.Saved[BatchOperationOrder.PowerPlan], "Failed dependent restoration did not retain the selected power plan.");
            var riskFake=new ReviewPowerBatch();
            var riskDefinition=riskFake.Definition("perf.usb-selective-suspend");
            riskDefinition.RiskLevel=TweakRiskLevel.High;
            riskDefinition.Name="Isolated high-risk confirmation review";
            riskDefinition.Description="This review exercises the production confirmation, apply and restore flow using an isolated fake service. No Windows settings, device state, files or real backups are changed.";
            var riskRow=new TweakRowViewModel(riskDefinition,riskFake);await riskRow.Initialization;
            async Task<Panel> Confirmation(){
                var timeout=Stopwatch.StartNew();
                while(root.Children.Count<3&&timeout.Elapsed<TimeSpan.FromSeconds(8))await Task.Delay(25);
                Check(root.Children.Count==3,"High-risk confirmation did not appear over the production progress overlay.");
                var overlay=(Panel)root.Children[2];
                Check(overlay.IsEnabled&&!workspace.IsEnabled,"Confirmation was disabled or underlying workspace was interactive.");
                return overlay;
            }
            var cancelled=ReviewBatch(true,riskRow);var confirmation=await Confirmation();
            await Export(main,Path.Combine(folder,"high-risk-confirmation.png"));
            Descendants<Button>(confirmation).Single(button=>Equals(button.Content,"Cancel")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await cancelled.WaitAsync(TimeSpan.FromSeconds(10));
            Check(riskFake.Calls.Count==0&&riskRow.StatusText.Contains("cancelled"),"Cancel executed an isolated high-risk change or lost cancellation status.");
            Check(root.Children.Count==1&&workspace.IsEnabled,"Cancel left an overlay or disabled workspace.");

            var escaped=ReviewBatch(true,riskRow);confirmation=await Confirmation();
            var escape=new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice,
                PresentationSource.FromVisual(main),Environment.TickCount,System.Windows.Input.Key.Escape){RoutedEvent=UIElement.PreviewKeyDownEvent};
            confirmation.RaiseEvent(escape);await escaped.WaitAsync(TimeSpan.FromSeconds(10));
            Check(escape.Handled&&riskFake.Calls.Count==0&&root.Children.Count==1&&workspace.IsEnabled,"Escape did not cancel cleanly without invoking the fake backend.");

            var accepted=ReviewBatch(true,riskRow);confirmation=await Confirmation();
            Descendants<Button>(confirmation).Single(button=>Equals(button.Content,"Apply tweak")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await accepted.WaitAsync(TimeSpan.FromSeconds(10));
            Check(riskFake.Calls.SequenceEqual(new[]{"apply:perf.usb-selective-suspend"})&&riskRow.IsApplied,"Accepted high-risk action did not call its fake backend exactly once.");
            Check(root.Children.Count==1&&workspace.IsEnabled,"Accepted confirmation left an overlay or disabled workspace.");
            await ReviewBatch(false,riskRow).WaitAsync(TimeSpan.FromSeconds(10));
            Check(riskFake.Calls.SequenceEqual(new[]{"apply:perf.usb-selective-suspend","restore:perf.usb-selective-suspend"})&&!riskRow.IsApplied,
                "High-risk original restoration did not run through the production restore flow.");
            Check(root.Children.Count==1&&workspace.IsEnabled,"Restoration left a confirmation or progress overlay.");
            var longest=App.Services.GetRequiredService<ITweakService>().GetAllTweaks().OrderByDescending(definition=>definition.Description.Length).First();
            riskDefinition.Name=longest.Name;riskDefinition.Description=longest.Description;
            riskFake.Calls.Clear();
            var longReview=ReviewBatch(true,riskRow);confirmation=await Confirmation();main.UpdateLayout();
            foreach(var button in Descendants<Button>(confirmation)){
                var topLeft=button.TranslatePoint(new Point(),root);
                Check(button.IsEnabled&&button.ActualHeight>0&&topLeft.Y>=0&&topLeft.Y+button.ActualHeight<=root.ActualHeight,
                    "A confirmation action is clipped or disabled with the longest production description.");
            }
            await Export(main,Path.Combine(folder,"high-risk-longest-description.png"));
            Descendants<Button>(confirmation).Single(button=>Equals(button.Content,"Cancel")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await longReview.WaitAsync(TimeSpan.FromSeconds(10));
            Check(riskFake.Calls.Count==0&&root.Children.Count==1&&workspace.IsEnabled,"Longest-description cancellation changed state or left an overlay.");
            File.WriteAllText(Path.Combine(folder, "result.txt"), $"PASS: {checks} isolated activation, production startup/apply layout and timing, failure cleanup, owner-state, batch dependencies and high-risk Cancel/Escape/Accept/Restore checks. Power and high-risk operations used a fake service. No real license activation or Windows tweak actions executed.");
            main.Close();
            Application.Current.Shutdown(0);
        }
        catch (Exception exception)
        {
            File.WriteAllText(Path.Combine(folder, "error.txt"), exception.ToString());
            Application.Current.Shutdown(78);
        }
        return true;
    }

    private static async Task Export(Window window, string path)
    {
        window.UpdateLayout();
        await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
        var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(path);
        encoder.Save(output);
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T:DependencyObject
    {
        if(parent is T match)yield return match;
        for(int index=0;index<VisualTreeHelper.GetChildrenCount(parent);index++)
            foreach(var child in Descendants<T>(VisualTreeHelper.GetChild(parent,index)))yield return child;
    }

    private sealed class ReviewPowerBatch : ITweakService
    {
        public Dictionary<string,bool> Applied = new() { [BatchOperationOrder.PowerPlan] = false, ["perf.usb-selective-suspend"] = false };
        public Dictionary<string,bool> Saved = new() { [BatchOperationOrder.PowerPlan] = false, ["perf.usb-selective-suspend"] = false };
        public List<string> Calls = new();
        public bool FailPlan, FailUsbRestore;
        public TweakDefinition Definition(string id) => new()
        {
            Id = id, Name = "Isolated power batch review", Description = "No Windows changes", Category = TweakCategory.Cpu,
            RiskLevel = TweakRiskLevel.Low, CurrentStateReader = () => Task.FromResult(Applied[id] ? TweakState.Applied : TweakState.NotApplied),
            HasSavedOriginal = () => Saved[id]
        };
        public Task<TweakActionResult> ApplyTweakAsync(string id)
        {
            Calls.Add("apply:" + id);
            if (id == BatchOperationOrder.PowerPlan && FailPlan) return Task.FromResult(TweakActionResult.Fail("Injected plan failure"));
            Applied[id] = Saved[id] = true; return Task.FromResult(TweakActionResult.Ok("Isolated apply"));
        }
        public Task<TweakActionResult> RestoreTweakAsync(string id)
        {
            Calls.Add("restore:" + id);
            if (id == "perf.usb-selective-suspend" && FailUsbRestore) return Task.FromResult(TweakActionResult.Fail("Injected USB restore failure"));
            Applied[id] = Saved[id] = false; return Task.FromResult(TweakActionResult.Ok("Isolated restore"));
        }
        public IReadOnlyList<TweakDefinition> GetAllTweaks() => Applied.Keys.Select(Definition).ToArray();
        public IReadOnlyList<TweakDefinition> GetTweaksByCategory(TweakCategory category) => GetAllTweaks();
        public Task<List<TweakDefinition>> GetRecommendedTweaksAsync() => Task.FromResult(new List<TweakDefinition>());
    }

    private sealed class ReviewLicense : ILicenseService
    {
        public int Activations;
        public string HardwareId => new('A', 64);
        public string FormattedHardwareId => "AAAA-AAAA-AAAA-AAAA-AAAA-AAAA-AAAA-AAAA";
        public string LicenseFilePath => "isolated-visual-review";
        public string? ReadSavedToken() => null;
        public LicenseValidationResult ValidateToken(string token, string? hwidOverride = null, DateTimeOffset? nowUtc = null) =>
            new(false, LicenseStatus.ConfigurationError, "Review key is invalid. Check your license and try again.");
        public Task<LicenseValidationResult> ActivateAsync(string token) { Activations++; return Task.FromResult(ValidateToken(token)); }
        public Task<LicenseValidationResult> ValidateSavedLicenseAsync() => Task.FromResult(ValidateToken(""));
        public Task<LicenseValidationResult> ValidateTokenOnlineAsync(string token) => Task.FromResult(ValidateToken(token));
    }
}
