using System;
using System.CodeDom.Compiler;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using QuantixTweakingUtility.Services;
using QuantixTweakingUtility.ViewModels;

namespace QuantixTweakingUtility;

public class MainWindow : Window, IComponentConnector
{
	private const double CornerRadiusPx = 12.0;

	private bool _contentLoaded;
	private readonly TextBlock _licenseStatus = new();

	public MainWindow(MainViewModel viewModel)
	{
		InitializeComponent();
		AddEliteHeader(viewModel);
        Loaded += (_, _) => VersionLabels.Refresh(this);
		base.DataContext = viewModel;
		base.Loaded += delegate
		{
			ApplyRoundedClip();
		};
		base.SizeChanged += delegate
		{
			ApplyRoundedClip();
		};
		base.StateChanged += delegate
		{
			ApplyRoundedClip();
		};
	}

	internal void SetLicenseExpiration(DateTimeOffset? expires) =>
		_licenseStatus.Text = expires is { } date ? $"Expires {date.ToLocalTime():dd MMM yyyy}" : "Lifetime license";

	private void AddEliteHeader(MainViewModel viewModel)
	{
		Title = "Elite";
		Icon = App.EliteLogo();
		Content = null;
		var shell = new DockPanel();
		var header = new DockPanel
		{
			Background = new SolidColorBrush(Color.FromRgb(24, 18, 38)),
			Margin = new Thickness(0, 0, 0, 1)
		};
		header.MouseLeftButtonDown += (_, e) =>
		{
			if (e.ClickCount == 2)
				MaximizeRestoreButton_Click(this, e);
			else
				DragMove();
		};
		var controls = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 6, 2) };
		foreach (var (symbol, label, action) in new (string, string, Action)[]
		{
			("−", "Minimize", () => WindowState = WindowState.Minimized),
			("□", "Maximize or restore", () => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized),
			("×", "Close", Close)
		})
		{
			var button = EliteWorkspace.MakeButton(symbol);
			button.ToolTip = label;
			button.Width = 30;
			button.Padding = new Thickness(0);
			button.Click += (_, _) => action();
			controls.Children.Add(button);
		}
		DockPanel.SetDock(controls, Dock.Right);
				header.Children.Add(controls);
		_licenseStatus.Foreground = Brushes.Lavender;
		_licenseStatus.FontSize = 11;
		_licenseStatus.VerticalAlignment = VerticalAlignment.Center;
		_licenseStatus.Margin = new Thickness(0, 0, 10, 0);
		DockPanel.SetDock(_licenseStatus, Dock.Right);
		header.Children.Add(_licenseStatus);
		var brand = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(16, 6, 0, 6) };
		brand.Children.Add(new Image { Source = Icon, Width = 23, Height = 23 });
		var pageTitle = new TextBlock
		{
			Foreground = Brushes.White, FontSize = 12, FontWeight = FontWeights.SemiBold,
			VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0)
		};
		void UpdatePageTitle() => pageTitle.Text = viewModel.SelectedNavItem?.Key switch
		{
			"dashboard" => "Home PC Stats",
			"basic" => "Windows Optimization",
			"network" => "Network Optimization",
			"gpu" => "GPU Optimization",
			"power" => "Power Optimization",
			"debloat" => "Debloat Optimization",
			"advanced" => "Extra Optimization",
			"cleanup" => "Debloat Optimization",
			_ => viewModel.SelectedNavItem?.Title ?? "Elite"
		};
		viewModel.PropertyChanged += (_, e) =>
		{
			if (e.PropertyName == nameof(MainViewModel.SelectedNavItem)) UpdatePageTitle();
		};
		UpdatePageTitle();
		brand.Children.Add(pageTitle);
		brand.Children.Add(new TextBlock
		{
			Text = "QUANTIX ELITE", Foreground = Brushes.Gray, FontSize = 10,
			VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0)
		});
		header.Children.Add(brand);
		DockPanel.SetDock(header, Dock.Top);
		shell.Children.Add(header);
				shell.Children.Add(new EliteWorkspace(viewModel, this));
		var root = new Grid();
		root.Children.Add(shell);
		Content = root;
		Width = 1050;
		Height = 660;
	}

	private void Window_SourceInitialized(object? sender, EventArgs e)
	{
		WindowCornerHelper.TryEnableRoundedCorners(new WindowInteropHelper(this).Handle);
	}

	private void ApplyRoundedClip()
	{
		if (!(base.ActualWidth <= 0.0) && !(base.ActualHeight <= 0.0))
		{
			base.Clip = ((base.WindowState == WindowState.Maximized) ? new RectangleGeometry(new Rect(0.0, 0.0, base.ActualWidth, base.ActualHeight)) : new RectangleGeometry(new Rect(0.0, 0.0, base.ActualWidth, base.ActualHeight), 12.0, 12.0));
		}
	}

	private void MinimizeButton_Click(object sender, RoutedEventArgs e)
	{
		base.WindowState = WindowState.Minimized;
	}

	private void MaximizeRestoreButton_Click(object sender, RoutedEventArgs e)
	{
		base.WindowState = ((base.WindowState != WindowState.Maximized) ? WindowState.Maximized : WindowState.Normal);
	}

	private void CloseButton_Click(object sender, RoutedEventArgs e)
	{
		Close();
	}

	private void ImportOfflineUpdate_Click(object sender, RoutedEventArgs e)
	{
		OfflineUpdateUi.Import(this);
	}

	[DebuggerNonUserCode]
	[GeneratedCode("PresentationBuildTasks", "10.0.12.0")]
	public void InitializeComponent()
	{
		if (!_contentLoaded)
		{
			_contentLoaded = true;
			Uri resourceLocator = new Uri("/QuantixTweakingUtility;component/quantixtweakingutility.mainwindow.xaml", UriKind.Relative);
			Application.LoadComponent(this, resourceLocator);
		}
	}

	[DebuggerNonUserCode]
	[GeneratedCode("PresentationBuildTasks", "10.0.12.0")]
	[EditorBrowsable(EditorBrowsableState.Never)]
	void IComponentConnector.Connect(int connectionId, object target)
	{
		switch (connectionId)
		{
		case 1:
			((MainWindow)target).SourceInitialized += Window_SourceInitialized;
			break;
		case 2:
			((Button)target).Click += MinimizeButton_Click;
			break;
		case 3:
			((Button)target).Click += MaximizeRestoreButton_Click;
			break;
		case 4:
			((Button)target).Click += CloseButton_Click;
			break;
		default:
			_contentLoaded = true;
			break;
		}
	}
}









