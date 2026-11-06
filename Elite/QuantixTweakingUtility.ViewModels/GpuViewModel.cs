using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel.__Internals;
using CommunityToolkit.Mvvm.Input;
using QuantixTweakingUtility.Models;
using QuantixTweakingUtility.Services;

namespace QuantixTweakingUtility.ViewModels;

public sealed class GpuViewModel : ObservableObject
{
	private static readonly string[] ProfileSettings = new string[28]
	{
		"Low Latency Mode: On", "Maximum pre-rendered frames: 1", "Preferred refresh rate: Highest available", "Power management mode: Prefer maximum performance", "Texture filtering quality: High performance", "Anisotropic filter optimization: On", "Anisotropic sample optimization: On", "Trilinear optimization: On", "Negative LOD bias: Allow", "Driver-controlled LOD bias: Off",
		"Anisotropic filtering mode: Application-controlled", "Anisotropic filtering setting: Application-controlled", "Antialiasing mode: Application-controlled", "Antialiasing setting: Application-controlled", "Antialiasing gamma correction: Off", "Antialiasing line gamma: Off", "FXAA usage: Disallowed", "Shader cache: On", "Shader cache size: Unlimited", "OpenGL shader cache: On",
		"Shader pre-compilation: Low", "Threaded optimization: Auto", "NVIDIA Ansel enabled: Off", "NVIDIA Ansel allowed: Off", "OpenGL telemetry monitor: Disabled", "OpenGL event logging: Disabled", "OpenGL GDI compatibility: Prefer disabled", "Memory allocation policy: Moderate pre-allocation"
	};

	private readonly IGpuToolService _gpuTools;

	private readonly TweakTabViewModel _tab;

	[ObservableProperty]
	private bool _isBusy;

	[ObservableProperty]
	private bool _nvidiaDetected;

	[ObservableProperty]
	private bool _amdDetected;

	[ObservableProperty]
	private string _nvidiaStatus = "Checking installed graphics hardware...";

	[ObservableProperty]
	private string _radeonStatus = "Checking installed graphics hardware...";

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.2.0.0")]
	private AsyncRelayCommand? installNvidiaProfileCommand;

	public TweakTabViewModel Tab => _tab;

	public string Title => _tab.Title;

	public string Description => _tab.Description;

	public int TweakCount => _tab.TweakCount + 9;

	public ObservableCollection<TweakGroupViewModel> Groups => _tab.Groups;

	public string ProfileSummary => string.Join("  •  ", ProfileSettings);

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.2.0.0")]
	[ExcludeFromCodeCoverage]
	public bool IsBusy
	{
		get
		{
			return _isBusy;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_isBusy, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.IsBusy);
				_isBusy = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.IsBusy);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.2.0.0")]
	[ExcludeFromCodeCoverage]
	public bool NvidiaDetected
	{
		get
		{
			return _nvidiaDetected;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_nvidiaDetected, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.NvidiaDetected);
				_nvidiaDetected = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.NvidiaDetected);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.2.0.0")]
	[ExcludeFromCodeCoverage]
	public bool AmdDetected
	{
		get
		{
			return _amdDetected;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_amdDetected, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.AmdDetected);
				_amdDetected = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.AmdDetected);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.2.0.0")]
	[ExcludeFromCodeCoverage]
	public string NvidiaStatus
	{
		get
		{
			return _nvidiaStatus;
		}
		[MemberNotNull("_nvidiaStatus")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_nvidiaStatus, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.NvidiaStatus);
				_nvidiaStatus = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.NvidiaStatus);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.2.0.0")]
	[ExcludeFromCodeCoverage]
	public string RadeonStatus
	{
		get
		{
			return _radeonStatus;
		}
		[MemberNotNull("_radeonStatus")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_radeonStatus, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.RadeonStatus);
				_radeonStatus = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.RadeonStatus);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.2.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand InstallNvidiaProfileCommand => installNvidiaProfileCommand ?? (installNvidiaProfileCommand = new AsyncRelayCommand(InstallNvidiaProfileAsync, CanInstallNvidiaProfile));

	public GpuViewModel(TweakTabViewModel tab, IGpuToolService gpuTools, ISystemInfoService systemInfo)
	{
		_tab = tab;
		_gpuTools = gpuTools;
		DetectHardwareAsync(systemInfo);
	}

	private async Task DetectHardwareAsync(ISystemInfoService systemInfo)
	{
		try
		{
			SystemSnapshot systemSnapshot = await systemInfo.GetStaticInfoAsync();
			NvidiaDetected = systemSnapshot.GpuName.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase);
			AmdDetected = systemSnapshot.GpuName.Contains("AMD", StringComparison.OrdinalIgnoreCase) || systemSnapshot.GpuName.Contains("Radeon", StringComparison.OrdinalIgnoreCase);
			NvidiaStatus = (NvidiaDetected ? "NVIDIA GPU detected. Ready to download the verified official tool and Quantix profile." : "No NVIDIA GPU detected. The Inspector action is disabled on this system.");
			RadeonStatus = (AmdDetected ? "AMD Radeon GPU detected. Radeon-specific controls are available below." : "No AMD Radeon GPU detected. Radeon controls are shown automatically on AMD systems.");
		}
		catch
		{
			NvidiaStatus = "Graphics hardware detection was unavailable.";
			RadeonStatus = "Graphics hardware detection was unavailable.";
		}
		finally
		{
			InstallNvidiaProfileCommand.NotifyCanExecuteChanged();
		}
	}

	private bool CanInstallNvidiaProfile()
	{
		if (NvidiaDetected)
		{
			return !IsBusy;
		}
		return false;
	}

	[RelayCommand(CanExecute = "CanInstallNvidiaProfile")]
	private async Task InstallNvidiaProfileAsync()
	{
		string text = string.Join(Environment.NewLine, ProfileSettings.Select((string setting) => "  •  " + setting));
		if (MessageBox.Show("Quantix will download NVIDIA Profile Inspector from Orbmu2k's official GitHub release, verify its SHA-256, and merge these settings into NVIDIA's Base Profile:\n\n" + text + "\n\nMerge mode preserves unrelated game profiles and settings. Continue?", "Import quantix.nip — Quantix NVIDIA Profile", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
		{
			return;
		}
		IsBusy = true;
		InstallNvidiaProfileCommand.NotifyCanExecuteChanged();
		NvidiaStatus = "Downloading and verifying the official NVIDIA Profile Inspector release...";
		try
		{
			GpuToolResult gpuToolResult = await _gpuTools.PrepareNvidiaProfileInspectorAsync();
			NvidiaStatus = gpuToolResult.Message;
			MessageBox.Show(gpuToolResult.Message, gpuToolResult.Success ? "NVIDIA Profile Ready" : "NVIDIA Profile Setup Failed", MessageBoxButton.OK, gpuToolResult.Success ? MessageBoxImage.Asterisk : MessageBoxImage.Hand);
		}
		catch (Exception ex)
		{
			NvidiaStatus = "NVIDIA profile setup could not complete: " + ex.Message + ". You can retry after checking the connection.";
		}
		finally
		{
			IsBusy = false;
			InstallNvidiaProfileCommand.NotifyCanExecuteChanged();
		}
	}
}

