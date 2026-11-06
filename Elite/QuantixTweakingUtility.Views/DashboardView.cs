using System;
using System.CodeDom.Compiler;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;

namespace QuantixTweakingUtility.Views;

public class DashboardView : UserControl, IComponentConnector
{
	private bool _contentLoaded;

	public DashboardView()
	{
		InitializeComponent();
	}

	private void Session_Click(object sender, RoutedEventArgs e)
	{
		GamingSessionWindow gamingSessionWindow = new GamingSessionWindow();
		gamingSessionWindow.Owner = Window.GetWindow(this);
		gamingSessionWindow.ShowDialog();
	}

	private void Cleanup_Click(object sender, RoutedEventArgs e)
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
			Uri resourceLocator = new Uri("/QuantixTweakingUtility;component/quantixtweakingutility.views.dashboardview.xaml", UriKind.Relative);
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
			((Button)target).Click += Session_Click;
			break;
		case 2:
			var oldButton = (Button)target; oldButton.Visibility = Visibility.Collapsed; DependencyObject parent = oldButton; while (parent != null && parent is not Border) parent = System.Windows.Media.VisualTreeHelper.GetParent(parent); if (parent is Border card) card.Visibility = Visibility.Collapsed;
			break;
		default:
			_contentLoaded = true;
			break;
		}
	}
}

