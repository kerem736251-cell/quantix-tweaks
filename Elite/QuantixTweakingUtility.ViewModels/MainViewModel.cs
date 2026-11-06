using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel.__Internals;
using CommunityToolkit.Mvvm.Input;
using QuantixTweakingUtility.Services;

namespace QuantixTweakingUtility.ViewModels;

public sealed class MainViewModel : ObservableObject
{
	[ObservableProperty]
	private NavItem? _selectedNavItem;

	[ObservableProperty]
	private object? _currentViewModel;

	private readonly Dictionary<string, object> _pages;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.2.0.0")]
	private RelayCommand? joinDiscordCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.2.0.0")]
	private RelayCommand? openYouTubeCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.2.0.0")]
	private RelayCommand? openTikTokCommand;

	public ObservableCollection<NavItem> NavItems { get; }

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.2.0.0")]
	[ExcludeFromCodeCoverage]
	public NavItem? SelectedNavItem
	{
		get
		{
			return _selectedNavItem;
		}
		set
		{
			if (!EqualityComparer<NavItem>.Default.Equals(_selectedNavItem, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.SelectedNavItem);
				_selectedNavItem = value;
				OnSelectedNavItemChanged(value);
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.SelectedNavItem);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.2.0.0")]
	[ExcludeFromCodeCoverage]
	public object? CurrentViewModel
	{
		get
		{
			return _currentViewModel;
		}
		set
		{
			if (!EqualityComparer<object>.Default.Equals(_currentViewModel, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.CurrentViewModel);
				_currentViewModel = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.CurrentViewModel);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.2.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand JoinDiscordCommand => joinDiscordCommand ?? (joinDiscordCommand = new RelayCommand(JoinDiscord));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.2.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand OpenYouTubeCommand => openYouTubeCommand ?? (openYouTubeCommand = new RelayCommand(OpenYouTube));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.2.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand OpenTikTokCommand => openTikTokCommand ?? (openTikTokCommand = new RelayCommand(OpenTikTok));

	internal TweakTabViewModel PowerTab => (TweakTabViewModel)_pages["power"];
	internal TweakTabViewModel PrecisionTab => (TweakTabViewModel)_pages["precision"];
	internal TweakTabViewModel ServicesTab => (TweakTabViewModel)_pages["service-groups"];
	internal TweakTabViewModel AppearanceTab => (TweakTabViewModel)_pages["appearance"];

	public MainViewModel(DashboardViewModel dashboard, ITweakService tweakService, IGpuToolService gpuToolService, ISystemInfoService systemInfoService, GamingViewModel gaming, CleanupViewModel cleanup, StartupManagerViewModel startupManager, SettingsViewModel settings)
	{
		IReadOnlyDictionary<string, TweakTabViewModel> readOnlyDictionary = TweakTabCatalog.Build(tweakService);
		_pages = new Dictionary<string, object>
		{
			["dashboard"] = dashboard,
			["basic"] = readOnlyDictionary["basic"],
			["processes"] = readOnlyDictionary["processes"],
			["privacy"] = readOnlyDictionary["privacy"],
			["debloat"] = readOnlyDictionary["debloat"],
			["power"] = readOnlyDictionary["power"],
			["gpu"] = new GpuViewModel(readOnlyDictionary["gpu"], gpuToolService, systemInfoService),
			["network"] = readOnlyDictionary["network"],
			["advanced"] = readOnlyDictionary["advanced"],
			["mitigations"] = readOnlyDictionary["mitigations"],
			["service-groups"] = readOnlyDictionary["services"],
			["appearance"] = readOnlyDictionary["appearance"],
			["precision"] = readOnlyDictionary["precision"],
			["bios"] = new BiosViewModel(),
			["games"] = gaming,
			["cleanup"] = cleanup,
			["startup-manager"] = startupManager,
			["settings"] = settings
		};
		NavItems = new ObservableCollection<NavItem>
		{
			new NavItem { Key = "dashboard", Title = "Home", IconGeometry = "M2,2 H9 V9 H2 Z M11,2 H18 V9 H11 Z M2,11 H9 V18 H2 Z M11,11 H18 V18 H11 Z" },
			new NavItem { Key = "basic", Title = "Basic", IconGeometry = "M3,10 H17 M10,3 V17 M5,5 L15,15 M15,5 L5,15" },
			new NavItem { Key = "processes", Title = "Process Lowering", IconGeometry = "M3,18 V11 H6 V18 Z M9,18 V6 H12 V18 Z M15,18 V2 H18 V18 Z" },
			new NavItem { Key = "network", Title = "Network", IconGeometry = "M2,8 C6,4 14,4 18,8 M5,11 C8,8 12,8 15,11 M8,14 C9,13 11,13 12,14 M10,17 L10,17" },
			new NavItem { Key = "debloat", Title = "Debloat", IconGeometry = "M4,5 H16 M6,5 V17 H14 V5 M8,2 H12" },
			new NavItem { Key = "gpu", Title = "GPU", IconGeometry = "M3,5 H17 V15 H3 Z M6,8 H10 V12 H6 Z M17,8 H19 M17,12 H19 M7,15 V18 M13,15 V18" },
			new NavItem { Key = "advanced", Title = "Advanced", IconGeometry = "M2,15 L7,9 L11,13 L18,4" }
		};
		SelectedNavItem = NavItems[0];
		CurrentViewModel = dashboard;
	}

	[RelayCommand]
	private void JoinDiscord()
	{
		OpenCommunityLink("https://discord.gg/quantix");
	}

	[RelayCommand]
	private void OpenYouTube()
	{
		OpenCommunityLink("https://www.youtube.com/@quantixtweaks");
	}

	[RelayCommand]
	private void OpenTikTok()
	{
		OpenCommunityLink("https://www.tiktok.com/@quantix_tweaks");
	}

	private static void OpenCommunityLink(string url)
	{
		try
		{
			Process.Start(new ProcessStartInfo
			{
				FileName = url,
				UseShellExecute = true
			});
		}
		catch
		{
			MessageBox.Show("Could not open your browser. Visit " + url, "Quantix community", MessageBoxButton.OK, MessageBoxImage.Asterisk);
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.2.0.0")]
	private void OnSelectedNavItemChanged(NavItem? value)
	{
		if (value != null && _pages.TryGetValue(value.Key, out var value2))
		{
			CurrentViewModel = value2;
		}
	}
}






