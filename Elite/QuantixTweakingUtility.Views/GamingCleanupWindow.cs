using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace QuantixTweakingUtility.Views;

public sealed class GamingCleanupWindow : Window
{
	public sealed class CleanupResult
	{
		public string Id { get; set; } = "";

		public string State { get; set; } = "";
	}

	private readonly StackPanel rows = new StackPanel();

	private readonly TextBox report = new TextBox
	{
		IsReadOnly = true,
		TextWrapping = TextWrapping.Wrap,
		VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
		MinHeight = 70.0,
		MaxHeight = 160.0,
		Margin = new Thickness(0.0, 12.0, 0.0, 0.0),
		Background = Brushes.Transparent,
		Foreground = Brushes.White,
		BorderThickness = new Thickness(0.0)
	};

	private readonly Button scan = new Button
	{
		Content = "SCAN OPTIONAL ITEMS"
	};

	private readonly Button apply = new Button
	{
		Content = "APPLY SELECTED",
		IsEnabled = false
	};

	private readonly Button restore = new Button
	{
		Content = "RESTORE SERVICES"
	};

	private readonly Dictionary<string, CheckBox> checks = new Dictionary<string, CheckBox>();

	private bool busy;
	private readonly ProgressBar loading = new() { Height = 8, Minimum = 0, Maximum = 100, Foreground = new SolidColorBrush(Color.FromRgb(173,143,255)), Background = new SolidColorBrush(Color.FromRgb(40,32,56)), Margin = new Thickness(0, 8, 0, 8), Visibility = Visibility.Collapsed };
	private readonly TextBlock progressText = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) };
	private readonly Button recommended = new() { Content = "SELECT RECOMMENDED" };
	private readonly TextBlock processCount = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 8) };
	private static readonly string[] Recommended = { "Microsoft.BingNews", "Microsoft.BingWeather", "Microsoft.Getstarted", "Microsoft.WindowsFeedbackHub", "Microsoft.Microsoft3DViewer", "Microsoft.MicrosoftOfficeHub", "Microsoft.BingFinance", "Microsoft.BingSports", "MapsBroker", "Fax", "RetailDemo", "WMPNetworkSvc" };
	public bool IsWorking => busy;
	public bool LoadingVisible => loading.Visibility == Visibility.Visible;
	public string ProgressText => progressText.Text;
	public string ReportText => report.Text;
	public Task VerifyReadOnlyScanAsync() => Execute("Scan");
	public void SetProgressForPreview(string text) { busy = true; loading.Visibility = Visibility.Visible; loading.IsIndeterminate = true; progressText.Text = text; }
	private static int CountProcesses() { var processes = Process.GetProcesses(); try { return processes.Length; } finally { foreach (var p in processes) p.Dispose(); } }

	public static readonly Dictionary<string, string> Catalog = new Dictionary<string, string>
	{
		["Microsoft.BingNews"] = "News — removes the news app.",
		["Microsoft.BingWeather"] = "Weather — removes the weather app.",
		["Microsoft.Getstarted"] = "Tips — removes Windows tips.",
		["Microsoft.People"] = "People — removes the contacts app; export any local data first.",
		["Microsoft.WindowsFeedbackHub"] = "Feedback Hub — removes Windows feedback submission.",
		["Microsoft.ZuneMusic"] = "Media Player / Groove — removes this music player; media files stay.",
		["Microsoft.ZuneVideo"] = "Movies & TV — removes this video player; media files stay.",
		["Clipchamp.Clipchamp"] = "Clipchamp — removes the video editor; export local projects first.",
		["MapsBroker"] = "Downloaded Maps Manager — disables downloaded/offline map management.",
		["Fax"] = "Fax — disables sending and receiving faxes on this PC.",
		["RetailDemo"] = "Retail Demo — disables store demonstration mode.",
		["Microsoft.WindowsMaps"] = "Maps — removes the Windows maps app and local app data.",
		["Microsoft.SkypeApp"] = "Skype — removes the legacy Skype app and local app data.",
		["Microsoft.Microsoft3DViewer"] = "3D Viewer — removes the 3D model viewer.",
		["Microsoft.MicrosoftOfficeHub"] = "Office / Microsoft 365 hub — removes the hub app, not desktop Office.",
		["Microsoft.YourPhone"] = "Phone Link — removes PC/phone integration and local app data.",
		["Microsoft.BingFinance"] = "Money — removes the legacy finance app.",
		["Microsoft.BingSports"] = "Sports — removes the legacy sports app.",
		["Microsoft.WindowsSoundRecorder"] = "Sound Recorder — removes the recording app; export recordings first.",
		["DiagTrack"] = "Connected User Experiences — disables this diagnostic telemetry service.",
		["WMPNetworkSvc"] = "Media sharing — disables Windows Media Player network sharing.",
		["RemoteRegistry"] = "Remote Registry — disables remote registry administration.",
        ["Microsoft.BingTravel"] = "Travel — removes the legacy travel app.",
        ["Microsoft.BingFoodAndDrink"] = "Food & Drink — removes the legacy recipes app.",
        ["Microsoft.BingHealthAndFitness"] = "Health & Fitness — removes the legacy fitness app.",
        ["Microsoft.549981C3F5F10"] = "Cortana — removes the retired standalone app, not Windows Search.",
        ["OptionalWidgets"] = "Widgets — disables the optional Windows 11 board using a supported policy; original saved.",
        ["OptionalNewsFeeds"] = "News and interests — disables the optional Windows 10 taskbar feed; original saved.",
        ["OptionalTaskFeedback"] = "Feedback submission scheduled trigger — saved before disabling.",
        ["OptionalTaskFeedbackScenario"] = "Feedback scenario scheduled trigger — saved before disabling.",
        ["OptionalTaskCeip"] = "Optional CEIP scheduled trigger — saved before disabling.",
        ["OptionalTaskUsbCeip"] = "Optional USB CEIP scheduled trigger — saved before disabling.",
        ["OptionalTaskMapsToast"] = "Offline maps notification scheduled trigger — saved before disabling.",
        ["OptionalTaskMapsUpdate"] = "Offline maps update scheduled trigger — saved before disabling.",
	};

	public GamingCleanupWindow()
	{
		base.Title = "Quantix Elite — Uninstall unneeded services and apps";
		base.Width = 840.0;
		base.Height = 780.0;
		base.MinWidth = 650.0;
		base.MinHeight = 500.0;
		base.WindowStartupLocation = WindowStartupLocation.CenterOwner;
		base.Background = new SolidColorBrush(Color.FromRgb(16, 16, 25));
		base.Foreground = Brushes.White;
		StackPanel stackPanel = new StackPanel
		{
			Margin = new Thickness(25.0)
		};
		stackPanel.Children.Add(new TextBlock
		{
			Text = "UNINSTALL UNNEEDED SERVICES AND APPS",
			FontSize = 23.0,
			FontWeight = FontWeights.Bold
		});
		stackPanel.Children.Add(new TextBlock
		{
			Text = "Remove optional apps and disable optional services you do not use. Nothing is selected automatically. This can reduce clutter; FPS gains are not guaranteed.\n\nApps are removed for the Windows account running Elite (" + Environment.UserName + "). Local app data may be lost. Reinstall apps from Microsoft Store; RESTORE SERVICES restores only service settings. Browsers, Discord, Steam, other launchers, Xbox/Gaming Services, anti-cheat, Store, audio, webcams, networking, updates, security and drivers are excluded.",
			TextWrapping = TextWrapping.Wrap,
			Margin = new Thickness(0.0, 12.0, 0.0, 16.0)
		});
		WrapPanel wrapPanel = new WrapPanel();
		Button[] array = new Button[4] { scan, recommended, apply, restore };
		foreach (Button button in array)
		{
			button.Margin = new Thickness(0.0, 0.0, 10.0, 12.0);
			button.Padding = new Thickness(14.0, 10.0, 14.0, 10.0);
			button.Style = TryFindResource("GhostButtonStyle") as Style;
			wrapPanel.Children.Add(button);
		}
		Button button2 = new Button
		{
			Content = "REVIEW RUNNING APPS / STARTUP",
			Style = (TryFindResource("GhostButtonStyle") as Style),
			Margin = new Thickness(0.0, 0.0, 0.0, 12.0),
			HorizontalAlignment = HorizontalAlignment.Left
		};
		button2.Click += delegate
		{
			GamingSessionWindow gamingSessionWindow = new GamingSessionWindow();
			gamingSessionWindow.Owner = this;
			gamingSessionWindow.ShowDialog();
		};
		stackPanel.Children.Add(button2);
		stackPanel.Children.Add(wrapPanel);
		stackPanel.Children.Add(processCount);
		stackPanel.Children.Add(loading);
		stackPanel.Children.Add(progressText);
		stackPanel.Children.Add(rows);
		stackPanel.Children.Add(report);
		base.Content = new ScrollViewer
		{
			Content = stackPanel,
			VerticalScrollBarVisibility = ScrollBarVisibility.Auto
		};
		recommended.IsEnabled = false;
		recommended.Click += (_, _) => { foreach (var item in checks) item.Value.IsChecked = Recommended.Contains(item.Key) && item.Value.IsEnabled; report.Text = "Recommended items selected. Review, then apply. Search and diagnostics remain optional."; };
		scan.Click += async delegate
		{
			await Execute("Scan");
		};
		apply.Click += async delegate
		{
			string[] array2 = (from x in checks
				where x.Value.IsChecked == true
				select x.Key).ToArray();
			if (array2.Length == 0)
			{
				report.Text = "Select at least one item.";
			}
			else if (ConfirmSelection(array2))
			{
				await Execute("Apply", array2);
			}
		};
		restore.Click += async delegate
		{
			await Execute("Restore");
		};
		base.Closing += delegate(object? _, CancelEventArgs e)
		{
			if (busy)
			{
				e.Cancel = true;
			}
		};
		processCount.Text = "Currently running processes: " + CountProcesses() + ". Process counts vary as applications start and stop.";
		report.Text = "Scan to see which optional items exist on this PC. Scanning makes no changes. A 60-80 process target cannot be guaranteed while keeping Windows and gaming dependencies. No terminal or Windows Settings windows are opened.";
	}

	private bool ConfirmSelection(string[] selected)
	{
		Window dialog = new Window
		{
			Title = "Review gaming cleanup",
			Owner = this,
			Width = 760.0,
			Height = 570.0,
			MinWidth = 600.0,
			MinHeight = 400.0,
			WindowStartupLocation = WindowStartupLocation.CenterOwner,
			Background = base.Background,
			Foreground = base.Foreground
		};
		Grid grid = new Grid
		{
			Margin = new Thickness(22.0)
		};
		grid.RowDefinitions.Add(new RowDefinition
		{
			Height = GridLength.Auto
		});
		grid.RowDefinitions.Add(new RowDefinition());
		grid.RowDefinitions.Add(new RowDefinition
		{
			Height = GridLength.Auto
		});
		grid.Children.Add(new TextBlock
		{
			Text = $"Apply {selected.Length} selected items?\nApp removal can delete local app data. Services are disabled with their original settings saved.",
			TextWrapping = TextWrapping.Wrap,
			Margin = new Thickness(0.0, 0.0, 0.0, 16.0)
		});
		ScrollViewer element = new ScrollViewer
		{
			VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
			Content = new TextBlock
			{
				Text = string.Join("\n\n", selected.Select((string x) => Catalog[x])),
				TextWrapping = TextWrapping.Wrap
			}
		};
		Grid.SetRow(element, 1);
		grid.Children.Add(element);
		StackPanel stackPanel = new StackPanel
		{
			Orientation = Orientation.Horizontal,
			HorizontalAlignment = HorizontalAlignment.Right,
			Margin = new Thickness(0.0, 16.0, 0.0, 0.0)
		};
		Button element2 = new Button
		{
			Content = "CANCEL",
			IsCancel = true,
			Style = (TryFindResource("GhostButtonStyle") as Style),
			Margin = new Thickness(0.0, 0.0, 12.0, 0.0)
		};
		Button button = new Button
		{
			Content = "APPLY THESE ITEMS",
			Style = (TryFindResource("PrimaryButtonStyle") as Style)
		};
		button.Click += delegate
		{
			dialog.DialogResult = true;
		};
		stackPanel.Children.Add(element2);
		stackPanel.Children.Add(button);
		Grid.SetRow(stackPanel, 2);
		grid.Children.Add(stackPanel);
		dialog.Content = grid;
		return dialog.ShowDialog() == true;
	}

	private async Task Execute(string mode, string[]? selected = null)
	{
		busy = true;
		int before = CountProcesses();
		recommended.IsEnabled = false;
		loading.Visibility = Visibility.Visible; loading.IsIndeterminate = true;
		progressText.Text = mode == "Scan" ? "Scanning optional apps and services..." : "Preparing cleanup...";
		Button button = scan;
		Button button2 = apply;
		bool flag = (restore.IsEnabled = false);
		bool isEnabled = (button2.IsEnabled = flag);
		button.IsEnabled = isEnabled;
		rows.IsEnabled = false;
		report.Text = ((mode == "Scan") ? "Scanning…" : "Working… Keep this window open.");
		try
		{
			CleanupResult[] array = await RunAsync(mode, selected ?? Array.Empty<string>(), new Progress<string>(text => {
				if (!busy) return;
				var parts = text.Split('|');
				if (parts.Length == 3 && int.TryParse(parts[0], out var step) && int.TryParse(parts[1], out var total) && total > 0) {
					loading.IsIndeterminate = false; loading.Value = Math.Clamp(100.0 * step / total, 0, 100);
					progressText.Text = $"{mode}: item {step + 1} of {total} - {parts[2]}";
				}
			}));
			if (mode == "Scan")
			{
				rows.Children.Clear();
				checks.Clear();
				CleanupResult[] array2 = array;
				foreach (CleanupResult cleanupResult in array2)
				{
					if (Catalog.TryGetValue(cleanupResult.Id, out var value))
					{
						CheckBox checkBox = new CheckBox
						{
							Content = new TextBlock
							{
								Text = value + "\n" + cleanupResult.State,
								TextWrapping = TextWrapping.Wrap
							},
							Margin = new Thickness(0.0, 6.0, 0.0, 12.0),
							Foreground = Brushes.White,
							IsEnabled = !cleanupResult.State.StartsWith("Failed:") && !cleanupResult.State.EndsWith(" / Disabled", StringComparison.Ordinal)
						};
						rows.Children.Add(checkBox);
						checks.Add(cleanupResult.Id, checkBox);
					}
				}
				report.Text = ((array.Length == 0) ? "No optional items found." : "Scan complete. Select only items you do not use.");
			}
			else
			{
				report.Text = ((array.Length == 0) ? "No saved services to restore." : string.Join("\n", array.Select((CleanupResult x) => x.Id + ": " + x.State)));
				checks.Clear();
				rows.Children.Clear();
			}
		}
		catch (Exception ex)
		{
			report.Text = "Cleanup could not complete: " + ex.Message + "\nScan again to refresh. Service backups are retained.";
			checks.Clear();
			rows.Children.Clear();
		}
		finally
		{
			busy = false;
			loading.Visibility = Visibility.Collapsed; progressText.Text = "Finished. Review results below; failed items are reported.";
			processCount.Text = (mode == "Scan" ? "Scan only - nothing changed. Observed process counts: " : "Observed process counts before / after: ") + $"{before} / {CountProcesses()}. Counts fluctuate. Compare with the same apps open after restarting Windows.";
			recommended.IsEnabled = checks.Count > 0;
			Button button3 = scan;
			Button button4 = restore;
			flag = (rows.IsEnabled = true);
			isEnabled = (button4.IsEnabled = flag);
			button3.IsEnabled = isEnabled;
			apply.IsEnabled = checks.Count > 0;
		}
	}

	public static async Task<CleanupResult[]> RunAsync(string mode, string[] selected, IProgress<string>? progress = null)
	{
		if (mode != "Scan" && mode != "Apply" && mode != "Restore")
		{
			throw new ArgumentException("Invalid operation.");
		}
		if (selected.Any((string x) => !Catalog.ContainsKey(x)))
		{
			throw new ArgumentException("Unknown cleanup target.");
		}
		using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Quantix.GamingCleanup.ps1") ?? throw new IOException("Cleanup script is missing.");
		using StreamReader reader = new StreamReader(stream);
		string text = await reader.ReadToEndAsync();
		string s = "& {" + text + "} -Mode '" + mode + "' -Selected @(" + string.Join(",", selected.Select((string x) => "'" + x + "'")) + ")";
		ProcessStartInfo startInfo = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32\\WindowsPowerShell\\v1.0\\powershell.exe"))
		{
			UseShellExecute = false,
			CreateNoWindow = true,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			RedirectStandardInput = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8, StandardInputEncoding = new UTF8Encoding(false),
			Arguments = "-NoLogo -NoProfile -NonInteractive -EncodedCommand " + Convert.ToBase64String(Encoding.Unicode.GetBytes("$ProgressPreference='SilentlyContinue'; [Console]::InputEncoding=[Text.UTF8Encoding]::new($false); [Console]::OutputEncoding=[Text.UTF8Encoding]::new($false); Invoke-Expression ([Console]::In.ReadToEnd())"))
		};
		using Process process = Process.Start(startInfo) ?? throw new IOException("Cannot start Windows PowerShell.");
		Task<string> stdout = process.StandardOutput.ReadToEndAsync();
		async Task<string> ReadErrorsAsync() {
			var errors = new StringBuilder(); string? line;
			while ((line = await process.StandardError.ReadLineAsync()) != null) {
				if (line.StartsWith("QUANTIX_PROGRESS|", StringComparison.Ordinal)) progress?.Report(line.Substring("QUANTIX_PROGRESS|".Length));
				else errors.AppendLine(line);
			}
			return errors.ToString();
		}
		Task<string> stderr = ReadErrorsAsync();
		await process.StandardInput.WriteAsync(s); process.StandardInput.Close();
		await process.WaitForExitAsync();
		string output = await stdout;
		string text2 = await stderr;
		if (process.ExitCode != 0)
		{
			throw new IOException(string.IsNullOrWhiteSpace(text2) ? "Windows rejected the operation." : text2);
		}
		return JsonSerializer.Deserialize<CleanupResult[]>(output) ?? Array.Empty<CleanupResult>();
	}
}
