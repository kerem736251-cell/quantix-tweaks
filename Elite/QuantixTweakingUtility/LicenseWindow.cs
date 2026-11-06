using System;
using System.CodeDom.Compiler;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Markup;
using QuantixTweakingUtility.Services;
using QuantixTweakingUtility.ViewModels;

namespace QuantixTweakingUtility;

public class LicenseWindow : Window, IComponentConnector
{
	private LicenseViewModel? _viewModel;

	private bool _contentLoaded;

	public LicenseWindow()
	{
		_contentLoaded = true;
		BuildActivationScreen();
	}

	public LicenseWindow(LicenseViewModel viewModel)
		: this()
	{
		_viewModel = viewModel;
		base.DataContext = viewModel;
		viewModel.LicenseAccepted += ViewModel_LicenseAccepted;
		viewModel.ExitRequested += ViewModel_ExitRequested;
		base.Closed += LicenseWindow_Closed;
	}

	private void BuildActivationScreen()
	{
		Title = "Elite · Activation";
		Width = 760;
		Height = 510;
		WindowStartupLocation = WindowStartupLocation.CenterScreen;
		ResizeMode = ResizeMode.NoResize;
		WindowStyle = WindowStyle.None;
		Background = new SolidColorBrush(Color.FromRgb(14, 12, 21));
		Icon = App.EliteLogo();
		var panel = new StackPanel { Width = 420, VerticalAlignment = VerticalAlignment.Center };
		panel.Children.Add(new Image { Source = Icon, Width = 64, Height = 64, Margin = new Thickness(0, 0, 0, 15) });
		panel.Children.Add(new TextBlock { Text = "Activate Elite", Foreground = Brushes.White, FontSize = 28,
			FontWeight = FontWeights.SemiBold, TextAlignment = TextAlignment.Center });
		panel.Children.Add(new TextBlock { Text = "Enter your license once to activate this PC.", Foreground = Brushes.LightGray,
			FontSize = 13, TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 7, 0, 22) });
		panel.Children.Add(new TextBlock { Text = "License key", Foreground = Brushes.LightGray,
			FontSize = 11, Margin = new Thickness(0, 0, 0, 6) });
		var key = new TextBox
		{
			AcceptsReturn = false, Height = 42, Padding = new Thickness(10), FontSize = 13,
			Background = new SolidColorBrush(Color.FromRgb(31, 25, 45)), Foreground = Brushes.White,
			BorderBrush = new SolidColorBrush(Color.FromRgb(108, 72, 159)), CaretBrush = Brushes.White
		};
		key.SetBinding(TextBox.TextProperty, new Binding(nameof(LicenseViewModel.LicenseKey))
			{ Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
		System.Windows.Automation.AutomationProperties.SetName(key, "License key");
		panel.Children.Add(key);
		var activate = EliteWorkspace.MakeButton("Activate license");
		activate.IsDefault = true;
		activate.Background = new SolidColorBrush(Color.FromRgb(119, 67, 217));
		activate.Margin = new Thickness(0, 10, 0, 0);
		activate.Padding = new Thickness(12);
		activate.SetBinding(Button.CommandProperty, new Binding(nameof(LicenseViewModel.ActivateCommand)));
		panel.Children.Add(activate);
		var status = new TextBlock { TextWrapping = TextWrapping.Wrap,
			TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 10, 0, 8) };
		var statusStyle = new Style(typeof(TextBlock));
		statusStyle.Setters.Add(new Setter(TextBlock.ForegroundProperty, Brushes.Lavender));
		var failed = new DataTrigger { Binding = new Binding(nameof(LicenseViewModel.HasError)), Value = true };
		failed.Setters.Add(new Setter(TextBlock.ForegroundProperty, new SolidColorBrush(Color.FromRgb(238, 143, 158))));
		statusStyle.Triggers.Add(failed);
		status.Style = statusStyle;
		status.SetBinding(TextBlock.TextProperty, new Binding(nameof(LicenseViewModel.StatusMessage)));
		panel.Children.Add(status);
		var hardware = new TextBox { IsReadOnly = true, Background = Brushes.Transparent, Foreground = Brushes.Gray,
			BorderThickness = new Thickness(0), TextAlignment = TextAlignment.Center, FontSize = 11 };
		hardware.SetBinding(TextBox.TextProperty, new Binding(nameof(LicenseViewModel.HardwareId)) { Mode = BindingMode.OneWay });
		panel.Children.Add(hardware);
		var copy = EliteWorkspace.MakeButton("Copy hardware ID");
		copy.HorizontalAlignment = HorizontalAlignment.Center;
		copy.SetBinding(Button.CommandProperty, new Binding(nameof(LicenseViewModel.CopyHardwareIdCommand)));
		panel.Children.Add(copy);
		var root = new Grid();
		root.Children.Add(panel);
		var close = EliteWorkspace.MakeButton("×");
		close.ToolTip = "Close";
		close.IsCancel = true;
		close.Width = 32;
		close.Height = 28;
		close.HorizontalAlignment = HorizontalAlignment.Right;
		close.VerticalAlignment = VerticalAlignment.Top;
		close.Margin = new Thickness(8);
		close.Click += (_, _) => Close();
		root.Children.Add(close);
		var dragArea = new Border { Height = 35, Background = Brushes.Transparent, VerticalAlignment = VerticalAlignment.Top,
			Margin = new Thickness(0, 0, 48, 0) };
		dragArea.MouseLeftButtonDown += (_, _) => DragMove();
		root.Children.Add(dragArea);
		Content = root;
		Loaded += (_, _) => key.Focus();
	}

	private void ViewModel_LicenseAccepted(object? sender, EventArgs e)
	{
		base.DialogResult = true;
	}

	private void ViewModel_ExitRequested(object? sender, EventArgs e)
	{
		base.DialogResult = false;
	}

	private void LicenseWindow_Closed(object? sender, EventArgs e)
	{
		if (_viewModel != null)
		{
			_viewModel.LicenseAccepted -= ViewModel_LicenseAccepted;
			_viewModel.ExitRequested -= ViewModel_ExitRequested;
		}
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
			Uri resourceLocator = new Uri("/QuantixTweakingUtility;component/quantixtweakingutility.licensewindow.xaml", UriKind.Relative);
			Application.LoadComponent(this, resourceLocator);
		}
	}

	[DebuggerNonUserCode]
	[GeneratedCode("PresentationBuildTasks", "10.0.12.0")]
	[EditorBrowsable(EditorBrowsableState.Never)]
	void IComponentConnector.Connect(int connectionId, object target)
	{
		_contentLoaded = true;
	}
}




