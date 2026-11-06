using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using QuantixLicensing;
using QuantixTweakingUtility.Services;
using QuantixTweakingUtility.ViewModels;
using QuantixTweakingUtility.Views;

namespace QuantixTweakingUtility;

public class App : Application
{
	private IHost? _host;

	private bool _contentLoaded;

	public static IServiceProvider Services => ((App)Application.Current)._host.Services;

	protected override async void OnStartup(StartupEventArgs e)
	{
		base.OnStartup(e);
        if (await NetworkDiagnostics.RunAsync(e.Args)) return;
		int num = Array.IndexOf(e.Args, "--import-license-update");
		if (num >= 0)
		{
			try
			{
				OfflineUpdateUi.ImportFile(e.Args[num + 1]);
				Shutdown(0);
				return;
			}
			catch
			{
				Shutdown(65);
				return;
			}
		}
		base.ShutdownMode = ShutdownMode.OnExplicitShutdown;
		if (e.Args.Contains<string>("--verify-views", StringComparer.OrdinalIgnoreCase))
		{
			new DashboardView();
			new PerformanceView();
			new GamingView();
			new NetworkView();
			new WindowsView();
			new CleanupView();
			new GamingCleanupWindow();
			new GamingSessionWindow();
			new SettingsView();
			new TweakTabView();
			new GpuView();
			new BiosView();
			new LicenseWindow();
			Shutdown(0);
			return;
		}
		if (TryGetOption(e.Args, "--verify-bios", out string value))
		{
			try
			{
				ArgumentException.ThrowIfNullOrWhiteSpace(value, "previewPath");
				BiosViewModel biosViewModel = new BiosViewModel();
				foreach (string motherboardBrand in biosViewModel.MotherboardBrands)
				{
					foreach (string cpuPlatform in biosViewModel.CpuPlatforms)
					{
						bool[] array = new bool[2] { false, true };
						foreach (bool flag in array)
						{
							bool[] array2 = new bool[2] { false, true };
							foreach (bool flag2 in array2)
							{
								bool[] array3 = new bool[2] { false, true };
								foreach (bool cStateExperiment in array3)
								{
									string text = BiosViewModel.BuildGuide(motherboardBrand, cpuPlatform, "Test board", "Test CPU", flag, flag2, cStateExperiment);
									bool flag3 = motherboardBrand.Contains("OEM") || cpuPlatform.StartsWith("Select") || cpuPlatform.StartsWith("Other");
									if (text.Contains("NO TUNING PRESET SELECTED") != flag3 || (!flag3 && text.Contains("Precision Boost Overdrive") != cpuPlatform.StartsWith("AMD")) || (!flag3 && text.Contains("Memory profiles are overclocking") != flag) || (text.Contains("Enabled (optional overclocking trial)") && (!flag2 || !cpuPlatform.StartsWith("AMD") || cpuPlatform.Contains("1000") || cpuPlatform.Contains("8000G") || cpuPlatform.Contains("5000 X3D"))) || (cpuPlatform.Contains("13th / 14th") && text.Contains("Disabled (comparison test only)")))
									{
										throw new InvalidOperationException("BIOS guide matrix failed.");
									}
								}
							}
						}
					}
				}
				biosViewModel.SelectedPlatform = "AMD AM5 — Ryzen 7000 / 9000 (non-X3D)";
				biosViewModel.PboSupportConfirmed = true;
				biosViewModel.MotherboardModel = "Test model";
				if (biosViewModel.PboSupportConfirmed)
				{
					throw new InvalidOperationException("Hardware change must reset PBO confirmation.");
				}
				biosViewModel.PboSupportConfirmed = true;
				biosViewModel.IncludeCStateExperiment = true;
				biosViewModel.IncludeMemoryProfile = true;
				Window window = new Window
				{
					Content = new BiosView
					{
						DataContext = biosViewModel
					},
					Width = 1100.0,
					Height = 800.0,
					ShowInTaskbar = false,
					Left = -10000.0,
					Top = -10000.0,
					WindowStartupLocation = WindowStartupLocation.Manual
				};
				window.Show();
				window.UpdateLayout();
				using (FileStream output = File.Create(value))
				{
					BiosView.ExportGuidePng(biosViewModel.GuideText, output);
				}
				window.Close();
				Shutdown(0);
				return;
			}
			catch
			{
				Shutdown(40);
				return;
			}
		}
		_host = Host.CreateDefaultBuilder().ConfigureServices(delegate(HostBuilderContext _, IServiceCollection services)
		{
			services.AddSingleton<ILoggingService, LoggingService>();
			services.AddSingleton<ISystemInfoService, SystemInfoService>();
			services.AddSingleton<IRestoreService, RestoreService>();
			services.AddSingleton<ITweakService, TweakEngineService>();
			services.AddSingleton<INetworkInfoService, NetworkInfoService>();
			services.AddSingleton<ICleanupService, CleanupService>();
			services.AddSingleton<IAppSettingsService, AppSettingsService>();
			services.AddSingleton<IWindowsMaintenanceService, WindowsMaintenanceService>();
			services.AddSingleton<IGameLibraryService, GameLibraryService>();
			services.AddSingleton<IStartupManagerService, StartupManagerService>();
			services.AddSingleton<IRevocationService, RevocationService>();
			services.AddSingleton<ILicenseService, LicenseService>();
			services.AddSingleton<IGpuToolService, GpuToolService>();
			services.AddSingleton<MainViewModel>();
			services.AddSingleton<DashboardViewModel>();
			services.AddSingleton<PerformanceViewModel>();
			services.AddSingleton<GamingViewModel>();
			services.AddSingleton<NetworkViewModel>();
			services.AddSingleton<WindowsViewModel>();
			services.AddSingleton<CleanupViewModel>();
			services.AddSingleton<StartupManagerViewModel>();
			services.AddSingleton<SettingsViewModel>();
			services.AddTransient<LicenseViewModel>();
			services.AddSingleton<MainWindow>();
			services.AddTransient<LicenseWindow>();
		}).Build();
		await _host.StartAsync();
        if (await NetworkDiagnostics.RunVisibleAsync(e.Args)) return;
        if (await CleanupDiagnostics.RunAsync(e.Args)) return;
        if (await ScewinDiagnostics.RunAsync(e.Args)) return;
        if (await GraphicsDiagnostics.RunAsync(e.Args)) return;
        if (await WorkspaceDiagnostics.RunAsync(e.Args)) return;
        if (await FlowDiagnostics.RunAsync(e.Args)) return;
		if (TryGetOption(e.Args, "--export-tweaks", out string catalogPath))
		{
			var catalog = Services.GetRequiredService<ITweakService>().GetAllTweaks().Select(tweak => new
			{
				tweak.Id, tweak.Name, tweak.Description, Category = tweak.Category.ToString(), Risk = tweak.RiskLevel.ToString(),
				tweak.RequiresAdministrator, tweak.RequiresRestart, tweak.RestoreOnly, tweak.IsOneTimeAction, tweak.AffectedSettings
			});
			File.WriteAllText(catalogPath, System.Text.Json.JsonSerializer.Serialize(catalog, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
			Shutdown(0);
			return;
		}
		if (e.Args.Contains<string>("--verify-host", StringComparer.OrdinalIgnoreCase))
		{
			Services.GetRequiredService<MainWindow>();
			LicenseWindow requiredService = Services.GetRequiredService<LicenseWindow>();
			requiredService.ShowInTaskbar = false;
			requiredService.WindowStartupLocation = WindowStartupLocation.Manual;
			requiredService.Left = -10000.0;
			requiredService.Top = -10000.0;
			requiredService.Show();
			requiredService.UpdateLayout();
			requiredService.Close();
			Shutdown(0);
			return;
		}
		if (e.Args.Contains<string>("--verify-gpu-tool", StringComparer.OrdinalIgnoreCase))
		{
			IGpuToolService gpuToolService = Services.GetRequiredService<IGpuToolService>();
			GpuToolResult result = Task.Run(() => gpuToolService.PrepareNvidiaProfileInspectorAsync(openInspector: false)).GetAwaiter().GetResult();
			Shutdown((!result.Success) ? 30 : 0);
			return;
		}
		if (TryGetOption(e.Args, "--verify-license", out string value2))
		{
			if (string.IsNullOrWhiteSpace(value2) || !File.Exists(value2))
			{
				Shutdown(64);
				return;
			}
			TryGetOption(e.Args, "--hwid-override", out string value3);
			DateTimeOffset? nowUtc = null;
			if (TryGetOption(e.Args, "--time-override", out string value4) && DateTimeOffset.TryParse(value4, out var result2))
			{
				nowUtc = result2;
			}
			LicenseValidationResult licenseValidationResult = Services.GetRequiredService<ILicenseService>().ValidateToken(File.ReadAllText(value2), value3, nowUtc);
			Shutdown((int)((!licenseValidationResult.IsValid) ? (20 + licenseValidationResult.Status) : LicenseStatus.Valid));
			return;
		}
		if (TryGetOption(e.Args, "--activate-license-file", out string value5))
		{
			LicenseValidationResult licenseValidationResult2 = await Services.GetRequiredService<ILicenseService>().ActivateAsync(File.ReadAllText(value5));
			Shutdown((int)((!licenseValidationResult2.IsValid) ? (20 + licenseValidationResult2.Status) : LicenseStatus.Valid));
			return;
		}
		if (((ReadOnlySpan<string>)e.Args).Contains("--verify-saved-license"))
		{
			LicenseValidationResult licenseValidationResult3 = await Services.GetRequiredService<ILicenseService>().ValidateSavedLicenseAsync();
			Shutdown((int)((!licenseValidationResult3.IsValid) ? (20 + licenseValidationResult3.Status) : LicenseStatus.Valid));
			return;
		}
		ILoggingService loggingService = Services.GetRequiredService<ILoggingService>();
		loggingService.Info("Application", "Quantix Tweaking Utility Elite starting up.");
		SessionRestorePoint.StartOnce(message => loggingService.Info("SystemRestore", message));
		Window window2 = (base.MainWindow = Services.GetRequiredService<MainWindow>());
		MainWindow mainWindow = (MainWindow)window2;
		ILicenseService requiredService3 = Services.GetRequiredService<ILicenseService>();
		var activation = await requiredService3.ValidateSavedLicenseAsync();
		bool hasSavedLicense = activation.IsValid;
		if (!hasSavedLicense && Services.GetRequiredService<LicenseWindow>().ShowDialog() != true)
		{
			Shutdown(2);
			return;
		}
		if (!hasSavedLicense)
			activation = await requiredService3.ValidateSavedLicenseAsync();
		if (!activation.IsValid)
		{
			Shutdown(3);
			return;
		}
		mainWindow.SetLicenseExpiration(activation.License?.ExpiresAtUtc);
		loggingService.Info("License", "License explicitly verified for this launch.");
		mainWindow.Loaded += delegate
		{
			loggingService.Info("Application", "Main window loaded and visible.");
		};
		mainWindow.Closed += delegate
		{
			loggingService.Info("Application", "Main window closed; shutting down normally.");
			Shutdown(0);
		};
		try
		{
			await ShowStartupAsync();
			mainWindow.Show();
			loggingService.Info("Application", "Main window show completed.");
		}
		catch (Exception ex)
		{
			loggingService.Error("Application", "Main window could not be displayed.", ex);
			MessageBox.Show("Quantix could not open the main window.\n\n" + ex.Message, "Quantix Elite — Startup Error", MessageBoxButton.OK, MessageBoxImage.Hand);
			Shutdown(4);
		}
	}

	internal static ImageSource EliteLogo() =>
		new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/QuantixTweakingUtility;component/assets/elite-brand.png"));

	internal static async Task ShowStartupAsync(Func<Window, Task>? review = null, bool hiddenPreview = false)
	{
		Action<int, string>? report = null;
		await ShowProgressAsync("Loading…", "Starting Elite…", async () =>
		{
			for (int tick = 1; tick <= 30; tick++)
			{
				await Task.Delay(100);
				report?.Invoke(tick, tick < 10 ? "Starting Elite…" : tick < 24 ? "Preparing your workspace…" : "Opening dashboard…");
				if (review != null && tick == 12)
					await review(Current.Windows.OfType<Window>().Last(window => window != Current.MainWindow && window.Title == "Elite"));
			}
		}, steps: 30, bindProgress: update => report = update, hiddenPreview: hiddenPreview);
	}

	internal static async Task<bool> ConfirmTweakAsync(string name, string description)
	{
		var owner = Current.MainWindow;
		if (owner?.Content is not Grid root)
			return MessageBox.Show(name + "\n\n" + description, "Confirm high-risk tweak",
				MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;
		var completion = new TaskCompletionSource<bool>();
		var panel = new StackPanel { Width = 420, Margin = new Thickness(25) };
		panel.Children.Add(new TextBlock { Text = name, Foreground = Brushes.White, FontSize = 22,
			FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
		panel.Children.Add(new TextBlock { Text = description, Foreground = Brushes.Lavender,
			TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 14, 0, 12) });
		panel.Children.Add(new TextBlock { Text = "This high-risk setting can affect security, compatibility, or stability.",
			Foreground = Brushes.LightGray, TextWrapping = TextWrapping.Wrap });
		var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
			Margin = new Thickness(0, 20, 0, 0) };
		Button? cancel = null;
		foreach (var (label, accepted) in new[] { ("Cancel", false), ("Apply tweak", true) })
		{
			var button = EliteWorkspace.MakeButton(label);
			button.Margin = new Thickness(8, 0, 0, 0);
			button.Click += (_, _) => completion.TrySetResult(accepted);
			if (!accepted) cancel = button;
			buttons.Children.Add(button);
		}
		panel.Children.Add(buttons);
		var overlay = new Grid { Background = new SolidColorBrush(Color.FromArgb(210, 0, 0, 0)) };
		overlay.PreviewKeyDown += (_, e) =>
		{
			if (e.Key == System.Windows.Input.Key.Escape)
			{
				completion.TrySetResult(false);
				e.Handled = true;
			}
		};
		overlay.Children.Add(new Border { Background = new SolidColorBrush(Color.FromRgb(26, 21, 39)),
			CornerRadius = new CornerRadius(14), Child = panel, VerticalAlignment = VerticalAlignment.Center,
			HorizontalAlignment = HorizontalAlignment.Center });
		var workspace = root.Children[0];
		bool wasEnabled = workspace.IsEnabled;
		workspace.IsEnabled = false;
		root.Children.Add(overlay);
		cancel?.Focus();
		try { return await completion.Task; }
		finally
		{
			root.Children.Remove(overlay);
			workspace.IsEnabled = wasEnabled;
		}
	}

	internal static async Task ShowProgressAsync(string title, string detail, Func<Task> operation, Window? owner = null, int steps = 0, Action<Action<int, string>>? bindProgress = null, bool hiddenPreview = false)
	{
		var accent = new SolidColorBrush(Color.FromRgb(151, 101, 255));
		var panel = new StackPanel { Width = owner == null ? 290 : 340, MinHeight = owner == null ? 0 : 156, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
		if (owner == null)
			panel.Children.Add(new Image { Source = EliteLogo(), Width = 60, Height = 60, Margin = new Thickness(0, 0, 0, 18) });
		panel.Children.Add(new TextBlock { Text = title, Foreground = Brushes.White, FontSize = owner == null ? 23 : 26,
			FontWeight = FontWeights.SemiBold, TextAlignment = TextAlignment.Center });
		var message = new TextBlock { Text = detail, Foreground = Brushes.LightGray, FontSize = 12,
			TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 7, 0, 19) };
		if (owner != null)
			panel.Children.Add(message);
		else
			panel.Children.Add(new TextBlock { Text = "Quantix Elite Optimization Panel", Foreground = Brushes.LightGray,
				FontSize = 11, TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 4, 0, 24) });
		var progress = new ProgressBar { IsIndeterminate = steps == 0, Maximum = Math.Max(1, steps),
			Height = 4, Width = owner == null ? 250 : 290, Foreground = accent,
            Background = new SolidColorBrush(Color.FromRgb(49, 38, 67)), BorderThickness = new Thickness(0) };
		panel.Children.Add(progress);
		var currentAction = new TextBlock { Text = "Preparing…", Foreground = Brushes.LightGray, FontSize = 11,
			TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 58, 0, 0) };
		if (owner != null) panel.Children.Add(currentAction);
		if (owner == null)
		{
			message.Margin = new Thickness(0, 10, 0, 0);
			panel.Children.Add(message);
		}
		bindProgress?.Invoke((completed, status) =>
		{
			progress.Value = completed;
			if (owner == null) message.Text = status;
			else
			{
				message.Text = $"{Math.Clamp(completed, 0, Math.Max(1, steps))} of {steps} processed";
				currentAction.Text = status;
			}
		});
		if (owner?.Content is Grid root)
		{
			var overlay = new Grid { Background = new SolidColorBrush(Color.FromArgb(200, 0, 0, 0)) };
			overlay.Children.Add(new Border
			{
				Background = new SolidColorBrush(Color.FromRgb(26, 21, 39)),
				BorderBrush = new SolidColorBrush(Color.FromRgb(57, 43, 79)),
				BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(14),
				Padding = new Thickness(24), Child = panel, HorizontalAlignment = HorizontalAlignment.Center,
				VerticalAlignment = VerticalAlignment.Center
			});
			var workspace = root.Children[0];
			bool wasEnabled = workspace.IsEnabled;
			workspace.IsEnabled = false;
			root.Children.Add(overlay);
			try
			{
				await root.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.Background);
				await Task.WhenAll(operation(), Task.Delay(2000));
			}
			finally
			{
				root.Children.Remove(overlay);
				workspace.IsEnabled = wasEnabled;
			}
			return;
		}
		var splash = new Window
		{
			Title = "Elite", Width = 1050, Height = 660, WindowStartupLocation = hiddenPreview ? WindowStartupLocation.Manual : WindowStartupLocation.CenterScreen,
			Left = hiddenPreview ? -12000 : double.NaN, Top = hiddenPreview ? -12000 : double.NaN, ShowActivated = !hiddenPreview,
			WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false,
			Background = new RadialGradientBrush(Color.FromRgb(36, 26, 59), Color.FromRgb(12, 12, 20))
			{ Center = new Point(0.66, 0.2), GradientOrigin = new Point(0.66, 0.2), RadiusX = 0.75, RadiusY = 0.85 },
			Content = panel
		};
		try
		{
			splash.Show();
			await splash.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.Background);
			await operation();
		}
		finally { splash.Close(); }
	}

	private static bool TryGetOption(IReadOnlyList<string> args, string option, out string? value)
	{
		value = null;
		for (int i = 0; i < args.Count; i++)
		{
			if (string.Equals(args[i], option, StringComparison.OrdinalIgnoreCase))
			{
				if (i + 1 < args.Count && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
				{
					value = args[i + 1];
				}
				return true;
			}
		}
		return false;
	}

	protected override void OnExit(ExitEventArgs e)
	{
		try { TimerResolutionLease.Shared.Dispose(); }
		catch(Exception error) { Debug.WriteLine("Timer request cleanup on exit: "+error.Message); }
		if (_host != null)
		{
			_host.Services.GetService<ILoggingService>()?.Info("Application", $"Application exit requested with code {e.ApplicationExitCode}.");
			_host.StopAsync().GetAwaiter().GetResult();
			_host.Dispose();
		}
		base.OnExit(e);
	}

	[DebuggerNonUserCode]
	[GeneratedCode("PresentationBuildTasks", "10.0.12.0")]
	public void InitializeComponent()
	{
		if (!_contentLoaded)
		{
			_contentLoaded = true;
			Uri resourceLocator = new Uri("/QuantixTweakingUtility;component/app.xaml", UriKind.Relative);
			Application.LoadComponent(this, resourceLocator);
		}
	}

	[STAThread]
	[DebuggerNonUserCode]
	[GeneratedCode("PresentationBuildTasks", "10.0.12.0")]
	public static void Main()
	{
		App app = new App();
		app.InitializeComponent();
		app.Run();
	}
}









