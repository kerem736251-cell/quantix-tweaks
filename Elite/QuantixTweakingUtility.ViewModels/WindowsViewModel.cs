using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel.__Internals;
using CommunityToolkit.Mvvm.Input;
using QuantixTweakingUtility.Models;
using QuantixTweakingUtility.Services;

namespace QuantixTweakingUtility.ViewModels;

public sealed class WindowsViewModel : ObservableObject
{
	private readonly IWindowsMaintenanceService _maintenance;

	[ObservableProperty]
	private string _osDisplay = "Detecting...";

	[ObservableProperty]
	private string _uptimeDisplay = "Detecting...";

	[ObservableProperty]
	private bool _isVerifying;

	[ObservableProperty]
	private string _verificationStatus = "Verification is read-only and never repairs system files.";

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.2.0.0")]
	private AsyncRelayCommand? verifySystemFilesCommand;

	public ObservableCollection<TweakRowViewModel> WindowsControls { get; }

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.2.0.0")]
	[ExcludeFromCodeCoverage]
	public string OsDisplay
	{
		get
		{
			return _osDisplay;
		}
		[MemberNotNull("_osDisplay")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_osDisplay, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.OsDisplay);
				_osDisplay = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.OsDisplay);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.2.0.0")]
	[ExcludeFromCodeCoverage]
	public string UptimeDisplay
	{
		get
		{
			return _uptimeDisplay;
		}
		[MemberNotNull("_uptimeDisplay")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_uptimeDisplay, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.UptimeDisplay);
				_uptimeDisplay = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.UptimeDisplay);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.2.0.0")]
	[ExcludeFromCodeCoverage]
	public bool IsVerifying
	{
		get
		{
			return _isVerifying;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_isVerifying, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.IsVerifying);
				_isVerifying = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.IsVerifying);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.2.0.0")]
	[ExcludeFromCodeCoverage]
	public string VerificationStatus
	{
		get
		{
			return _verificationStatus;
		}
		[MemberNotNull("_verificationStatus")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_verificationStatus, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.VerificationStatus);
				_verificationStatus = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.VerificationStatus);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.2.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand VerifySystemFilesCommand => verifySystemFilesCommand ?? (verifySystemFilesCommand = new AsyncRelayCommand(VerifySystemFilesAsync));

	public WindowsViewModel(ITweakService tweaks, ISystemInfoService systemInfo, IWindowsMaintenanceService maintenance)
	{
		_maintenance = maintenance;
		WindowsControls = new ObservableCollection<TweakRowViewModel>(from tweak in tweaks.GetTweaksByCategory(TweakCategory.Windows)
			select new TweakRowViewModel(tweak, tweaks));
		InitializeAsync(systemInfo);
	}

	private async Task InitializeAsync(ISystemInfoService systemInfo)
	{
		SystemSnapshot systemSnapshot = await systemInfo.GetStaticInfoAsync();
		OsDisplay = systemSnapshot.OsDisplay;
		UptimeDisplay = ((systemSnapshot.Uptime.TotalHours >= 1.0) ? $"{(int)systemSnapshot.Uptime.TotalHours}h {systemSnapshot.Uptime.Minutes}m" : $"{systemSnapshot.Uptime.Minutes}m");
	}

	[RelayCommand]
	private async Task VerifySystemFilesAsync()
	{
		if (IsVerifying)
		{
			return;
		}
		IsVerifying = true;
		VerificationStatus = "Verifying protected Windows system files...";
		try
		{
			VerificationStatus = (await _maintenance.VerifySystemFilesAsync()).Item2;
		}
		finally
		{
			IsVerifying = false;
		}
	}
}
