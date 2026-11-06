using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Extensions.DependencyInjection;
using QuantixTweakingUtility.ViewModels;

namespace QuantixTweakingUtility.Views;

public sealed class GamingSessionWindow : Window
{
	public sealed record Snapshot(int Id, string Name, long Started, int Session, long WorkingSet);

	public static readonly HashSet<string> ClosableNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
	{
		"chrome", "msedge", "firefox", "brave", "opera", "vivaldi", "Discord", "DiscordCanary", "DiscordPTB", "Spotify",
		"Teams", "ms-teams", "OneDrive", "PhoneExperienceHost", "NVIDIA Overlay", "Skype"
	};

	private readonly Dictionary<CheckBox, Snapshot[]> choices = new Dictionary<CheckBox, Snapshot[]>();

	private readonly StackPanel rows = new StackPanel();

	private readonly TextBlock summary = new TextBlock
	{
		TextWrapping = TextWrapping.Wrap,
		Margin = new Thickness(0.0, 12.0, 0.0, 12.0)
	};

	private readonly TextBox report = new TextBox
	{
		IsReadOnly = true,
		TextWrapping = TextWrapping.Wrap,
		MaxHeight = 150.0,
		VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
		Background = Brushes.Transparent,
		Foreground = Brushes.White,
		BorderThickness = new Thickness(0.0),
		Margin = new Thickness(0.0, 0.0, 0.0, 8.0)
	};

	private readonly CheckBox force = new CheckBox
	{
		Content = "Force close selected apps (unsaved work, calls, sync or recordings can be lost)",
		Foreground = Brushes.White,
		Margin = new Thickness(0.0, 8.0, 0.0, 12.0)
	};

	private readonly Button refresh = new Button
	{
		Content = "REFRESH PROCESSES"
	};

	private readonly Button close = new Button
	{
		Content = "CLOSE SELECTED APPS"
	};

	private bool busy;

	public GamingSessionWindow()
	{
		base.Title = "Quantix Elite — Gaming session";
		base.Width = 880.0;
		base.Height = 780.0;
		base.MinWidth = 720.0;
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
			Text = "LESS RUNNING WHILE YOU PLAY",
			FontSize = 25.0,
			FontWeight = FontWeights.Bold
		});
		stackPanel.Children.Add(new TextBlock
		{
			Text = "Review this PC's processes. Closing apps lasts for this session; use Startup Manager to prevent supported apps from launching at sign-in. Nothing is closed automatically.\n\nProcess count alone does not measure gaming performance. Windows separates services into multiple hosts. A lower count or FPS improvement is not guaranteed.",
			TextWrapping = TextWrapping.Wrap,
			Margin = new Thickness(0.0, 12.0, 0.0, 8.0)
		});
		WrapPanel wrapPanel = new WrapPanel();
		Button button = new Button
		{
			Content = "STARTUP MANAGER"
		};
		Button[] array = new Button[3] { refresh, close, button };
		foreach (Button button2 in array)
		{
			button2.Style = TryFindResource("GhostButtonStyle") as Style;
			button2.Margin = new Thickness(0.0, 0.0, 10.0, 8.0);
			wrapPanel.Children.Add(button2);
		}
		stackPanel.Children.Add(wrapPanel);
		stackPanel.Children.Add(force);
		stackPanel.Children.Add(summary);
		stackPanel.Children.Add(report);
		stackPanel.Children.Add(rows);
		base.Content = new ScrollViewer
		{
			Content = stackPanel,
			VerticalScrollBarVisibility = ScrollBarVisibility.Auto
		};
		refresh.Click += delegate
		{
			Scan();
		};
		close.Click += async delegate
		{
			await CloseSelected();
		};
		button.Click += delegate
		{
			Window window = new Window();
			window.Title = "Quantix Elite — Startup Manager";
			window.Width = 1050.0;
			window.Height = 760.0;
			window.Owner = this;
			window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
			window.Background = base.Background;
			window.Content = new StartupManagerView
			{
				DataContext = App.Services.GetRequiredService<StartupManagerViewModel>()
			};
			window.ShowDialog();
		};
		base.Loaded += delegate
		{
			Scan();
		};
		base.Closing += delegate(object? _, CancelEventArgs e)
		{
			if (busy)
			{
				e.Cancel = true;
			}
		};
	}

	public static bool CanClose(Snapshot item, int session)
	{
		if (item.Started > 0 && item.Session == session)
		{
			return ClosableNames.Contains(item.Name);
		}
		return false;
	}

	public static List<Snapshot> Capture(out int total)
	{
		List<Snapshot> list = new List<Snapshot>();
		Process[] processes = Process.GetProcesses();
		total = processes.Length;
		Process[] array = processes;
		foreach (Process process in array)
		{
			using (process)
			{
				try
				{
					string processName = process.ProcessName;
					long started = 0L;
					long workingSet = 0L;
					int session = -1;
					try
					{
						workingSet = process.WorkingSet64;
						session = process.SessionId;
						started = process.StartTime.ToUniversalTime().Ticks;
					}
					catch
					{
					}
					list.Add(new Snapshot(process.Id, processName, started, session, workingSet));
				}
				catch
				{
				}
			}
		}
		return list;
	}

	private void Scan()
	{
		int total;
		List<Snapshot> source = Capture(out total);
		Process self = Process.GetCurrentProcess();
		try
		{
			rows.Children.Clear();
			choices.Clear();
			int num = 0;
			foreach (IGrouping<string, Snapshot> item in from x in source.GroupBy<Snapshot, string>((Snapshot x) => x.Name, StringComparer.OrdinalIgnoreCase)
				orderby x.Any((Snapshot p) => CanClose(p, self.SessionId)) descending, x.Count() descending
				select x)
			{
				Snapshot[] array = item.Where((Snapshot x) => CanClose(x, self.SessionId)).ToArray();
				num += array.Length;
				string text = $"{item.Key}  ·  {item.Count()} processes  ·  {item.Sum((Snapshot x) => x.WorkingSet) / 1024 / 1024:N0} MB working set";
				if (array.Length != 0)
				{
					CheckBox checkBox = new CheckBox
					{
						Content = new TextBlock
						{
							Text = text,
							TextWrapping = TextWrapping.Wrap
						},
						Foreground = Brushes.White,
						Margin = new Thickness(0.0, 7.0, 0.0, 7.0)
					};
					rows.Children.Add(checkBox);
					choices.Add(checkBox, array);
				}
				else
				{
					rows.Children.Add(new TextBlock
					{
						Text = text + "  ·  view only",
						Foreground = Brushes.Gray,
						TextWrapping = TextWrapping.Wrap,
						Margin = new Thickness(0.0, 5.0, 0.0, 5.0)
					});
				}
			}
			summary.Text = $"{total} total processes now. {num} processes belong to optional apps in this session. Shared memory can be counted more than once.\nBrowsers: tabs/downloads may close. Discord/Teams/Skype: calls end. OneDrive: sync pauses. NVIDIA Overlay: recording, replay and overlay stop. Windows, games, launchers and driver services are view only.";
		}
		finally
		{
			if (self != null)
			{
				((IDisposable)self).Dispose();
			}
		}
	}

	private async Task CloseSelected()
	{
		Snapshot[] selected = choices.Where((KeyValuePair<CheckBox, Snapshot[]> x) => x.Key.IsChecked == true).SelectMany((KeyValuePair<CheckBox, Snapshot[]> x) => x.Value).ToArray();
		if (selected.Length == 0)
		{
			report.Text = "Select apps to close first.";
			return;
		}
		bool forced = force.IsChecked == true;
		if (MessageBox.Show(this, (forced ? "Force close" : "Request normal close for") + " these apps?\n\n" + string.Join(", ", selected.Select((Snapshot x) => x.Name).Distinct()) + "\n\nSave your work first. Calls, downloads, sync and recordings may stop.", "Review gaming session", MessageBoxButton.YesNo, MessageBoxImage.Exclamation) != MessageBoxResult.Yes)
		{
			return;
		}
		busy = true;
		Button button = refresh;
		Button button2 = close;
		StackPanel stackPanel = rows;
		bool flag = (force.IsEnabled = false);
		bool flag3 = (stackPanel.IsEnabled = flag);
		bool isEnabled = (button2.IsEnabled = flag3);
		button.IsEnabled = isEnabled;
		try
		{
			using Process self = Process.GetCurrentProcess();
			int session = self.SessionId;
			string[] value = await Task.Run(() => selected.Select((Snapshot x) => CloseOne(x, session, forced)).ToArray());
			report.Text = string.Join("\n", value);
			Scan();
		}
		finally
		{
			busy = false;
			Button button3 = refresh;
			Button button4 = close;
			StackPanel stackPanel2 = rows;
			flag = (force.IsEnabled = true);
			flag3 = (stackPanel2.IsEnabled = flag);
			isEnabled = (button4.IsEnabled = flag3);
			button3.IsEnabled = isEnabled;
		}
	}

	public static string CloseOne(Snapshot item, int session, bool forced)
	{
		if (!CanClose(item, session))
		{
			return item.Name + ": protected / different session; skipped";
		}
		try
		{
			using Process process = Process.GetProcessById(item.Id);
			if (process.ProcessName != item.Name || process.StartTime.ToUniversalTime().Ticks != item.Started || process.SessionId != session)
			{
				return item.Name + ": process changed; refresh first";
			}
			if (forced)
			{
				process.Kill();
			}
			else if (!process.CloseMainWindow())
			{
				return item.Name + ": no main window; exit via its tray menu or choose force close";
			}
			return process.WaitForExit(1500) ? (item.Name + ": closed") : (item.Name + ": still running; save prompts or tray activity may need attention");
		}
		catch (ArgumentException)
		{
			return item.Name + ": already exited";
		}
		catch (Exception ex2)
		{
			return item.Name + ": " + ex2.Message;
		}
	}
}
