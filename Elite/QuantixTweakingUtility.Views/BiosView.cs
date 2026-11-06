using System;
using System.CodeDom.Compiler;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using QuantixTweakingUtility.ViewModels;

namespace QuantixTweakingUtility.Views;

public class BiosView : UserControl, IComponentConnector
{
	private bool _contentLoaded;

	public BiosView()
	{
		InitializeComponent();
		var original=(DependencyObject)Content;
		var scroll=Find<ScrollViewer>(original);
		var panel=new ScewinPanel();
		if(scroll!=null && scroll.Content is UIElement body) {
			var marker=Find<TextBlock>(body,t=>t.Text=="IN-APP BIOS CONTROL");
			DependencyObject? card=marker;StackPanel? target=null;UIElement? anchor=null;
			while(card!=null && card!=scroll) {if(card is UIElement element && LogicalTreeHelper.GetParent(card) is StackPanel stack){target=stack;anchor=element;}card=LogicalTreeHelper.GetParent(card);}
			if(target!=null && anchor!=null)target.Children.Insert(target.Children.IndexOf(anchor)+1,panel);
			else {scroll.Content=null;var stackBody=new StackPanel();stackBody.Children.Add(body);stackBody.Children.Add(panel);scroll.Content=stackBody;}
		} else {
			Content=null;var stack=new StackPanel{Margin=new Thickness(25)};stack.Children.Add((UIElement)original);stack.Children.Add(panel);Content=new ScrollViewer{Content=stack,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
		}
	}
	private static T? Find<T>(DependencyObject root,Func<T,bool>? predicate=null) where T:DependencyObject {
		if(root is T value && (predicate==null || predicate(value)))return value;
		foreach(var child in LogicalTreeHelper.GetChildren(root))if(child is DependencyObject node && Find<T>(node,predicate) is T found)return found;
		return null;
	}

	private async void Scanner_Loaded(object sender, RoutedEventArgs e)
	{
		if (base.DataContext is BiosViewModel { ScanResult: null } biosViewModel && !biosViewModel.ScanCommand.IsRunning)
		{
			await biosViewModel.ScanCommand.ExecuteAsync(null);
		}
	}

	private void CopyScan_Click(object sender, RoutedEventArgs e)
	{
		if (!(base.DataContext is BiosViewModel biosViewModel))
		{
			return;
		}
		try
		{
			Clipboard.SetText(biosViewModel.ScanReport);
		}
		catch
		{
			MessageBox.Show("The clipboard is busy. Please try again.", "Quantix");
		}
	}

	private void CopyText_Click(object sender, RoutedEventArgs e)
	{
		if (!(base.DataContext is BiosViewModel biosViewModel))
		{
			return;
		}
		try
		{
			Clipboard.SetText(biosViewModel.GuideText);
			biosViewModel.ExportStatus = "BIOS checklist copied.";
		}
		catch (Exception ex)
		{
			biosViewModel.ExportStatus = "Could not copy the checklist: " + ex.Message;
		}
	}

	private void SavePng_Click(object sender, RoutedEventArgs e)
	{
		if (!(base.DataContext is BiosViewModel biosViewModel))
		{
			return;
		}
		SaveFileDialog saveFileDialog = new SaveFileDialog
		{
			FileName = "quantix-bios.png",
			Filter = "PNG image|*.png",
			DefaultExt = ".png"
		};
		if (saveFileDialog.ShowDialog(Window.GetWindow(this)) != true)
		{
			return;
		}
		try
		{
			using FileStream output = File.Create(saveFileDialog.FileName);
			ExportGuidePng(biosViewModel.GuideText, output);
			biosViewModel.ExportStatus = "Checklist saved. Open it on another device before restarting into BIOS.";
		}
		catch (Exception ex)
		{
			biosViewModel.ExportStatus = "Could not save the checklist: " + ex.Message;
		}
	}

	public static void ExportGuidePng(string guide, Stream output)
	{
		Border border = new Border
		{
			Width = 1000.0,
			Padding = new Thickness(42.0),
			Background = new SolidColorBrush(Color.FromRgb(14, 10, 24)),
			Child = new TextBlock
			{
				Text = guide,
				TextWrapping = TextWrapping.Wrap,
				FontSize = 18.0,
				LineHeight = 28.0,
				FontFamily = new FontFamily("Segoe UI"),
				Foreground = Brushes.White
			}
		};
		border.Measure(new Size(1000.0, double.PositiveInfinity));
		border.Arrange(new Rect(default(Point), border.DesiredSize));
		border.UpdateLayout();
		RenderTargetBitmap renderTargetBitmap = new RenderTargetBitmap(1000, (int)Math.Ceiling(border.ActualHeight), 96.0, 96.0, PixelFormats.Pbgra32);
		renderTargetBitmap.Render(border);
		PngBitmapEncoder pngBitmapEncoder = new PngBitmapEncoder();
		pngBitmapEncoder.Frames.Add(BitmapFrame.Create(renderTargetBitmap));
		pngBitmapEncoder.Save(output);
	}

	[DebuggerNonUserCode]
	[GeneratedCode("PresentationBuildTasks", "10.0.12.0")]
	public void InitializeComponent()
	{
		if (!_contentLoaded)
		{
			_contentLoaded = true;
			Uri resourceLocator = new Uri("/QuantixTweakingUtility;component/quantixtweakingutility.views.biosview.xaml", UriKind.Relative);
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
			((BiosView)target).Loaded += Scanner_Loaded;
			break;
		case 2:
			((Button)target).Click += CopyScan_Click;
			break;
		default:
			_contentLoaded = true;
			break;
		}
	}
}
