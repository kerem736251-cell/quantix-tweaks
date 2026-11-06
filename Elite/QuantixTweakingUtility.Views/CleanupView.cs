using System;
using System.CodeDom.Compiler;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;

namespace QuantixTweakingUtility.Views;

public class CleanupView : UserControl, IComponentConnector
{
	private bool _contentLoaded;

	public CleanupView()
	{
		InitializeComponent();
		var original = (UIElement)Content;
		Content = null;
		var grid = new Grid();
		grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
		grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
		var automatic = new AutomaticCleanupPanel(); grid.Children.Add(automatic); Grid.SetRow(original, 1); grid.Children.Add(original); Content = grid;
	}

	private void GamingCleanup_Click(object sender, RoutedEventArgs e)
	{
		GamingCleanupWindow gamingCleanupWindow = new GamingCleanupWindow();
		gamingCleanupWindow.Owner = Window.GetWindow(this);
		gamingCleanupWindow.ShowDialog();
	}

	[DebuggerNonUserCode]
	[GeneratedCode("PresentationBuildTasks", "10.0.12.0")]
	public void InitializeComponent()
	{
		if (!_contentLoaded)
		{
			_contentLoaded = true;
			Uri resourceLocator = new Uri("/QuantixTweakingUtility;component/quantixtweakingutility.views.cleanupview.xaml", UriKind.Relative);
			Application.LoadComponent(this, resourceLocator);
		}
	}

	[DebuggerNonUserCode]
	[GeneratedCode("PresentationBuildTasks", "10.0.12.0")]
	[EditorBrowsable(EditorBrowsableState.Never)]
	void IComponentConnector.Connect(int connectionId, object target)
	{
		if (connectionId == 1)
		{
			var oldButton = (Button)target; oldButton.Visibility = Visibility.Collapsed; DependencyObject parent = oldButton; while (parent != null && parent is not Border) parent = System.Windows.Media.VisualTreeHelper.GetParent(parent); if (parent is Border card) card.Visibility = Visibility.Collapsed;
		}
		else
		{
			_contentLoaded = true;
		}
	}
}


