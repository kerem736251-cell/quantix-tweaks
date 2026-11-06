using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel.__Internals;
using QuantixTweakingUtility.Models;
using QuantixTweakingUtility.Services;

namespace QuantixTweakingUtility.ViewModels;

public sealed class DashboardViewModel : ObservableObject
{
	private readonly ISystemInfoService _systemInfo;

	private readonly ILoggingService _logging;

	[ObservableProperty]
	private string _cpuName = "Detecting...";

	[ObservableProperty]
	private string _gpuName = "Detecting...";

	[ObservableProperty]
	private string _ramTotalDisplay = "Detecting...";

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.2.0.0")]
	[ExcludeFromCodeCoverage]
	public string CpuName
	{
		get
		{
			return _cpuName;
		}
		[MemberNotNull("_cpuName")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_cpuName, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.CpuName);
				_cpuName = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.CpuName);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.2.0.0")]
	[ExcludeFromCodeCoverage]
	public string GpuName
	{
		get
		{
			return _gpuName;
		}
		[MemberNotNull("_gpuName")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_gpuName, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.GpuName);
				_gpuName = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.GpuName);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.2.0.0")]
	[ExcludeFromCodeCoverage]
	public string RamTotalDisplay
	{
		get
		{
			return _ramTotalDisplay;
		}
		[MemberNotNull("_ramTotalDisplay")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_ramTotalDisplay, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.RamTotalDisplay);
				_ramTotalDisplay = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.RamTotalDisplay);
			}
		}
	}

	public DashboardViewModel(ISystemInfoService systemInfo, ILoggingService logging)
	{
		_systemInfo = systemInfo;
		_logging = logging;
		InitializeAsync();
	}

	private async Task InitializeAsync()
	{
		try
		{
			SystemSnapshot systemSnapshot = await _systemInfo.GetStaticInfoAsync();
			CpuName = systemSnapshot.CpuName;
			GpuName = systemSnapshot.GpuName;
			RamTotalDisplay = systemSnapshot.RamTotalDisplay;
		}
		catch (Exception ex)
		{
			_logging.Error("DashboardViewModel", "Failed to detect hardware identity.", ex);
			CpuName = "Unknown CPU";
			GpuName = "Unknown GPU";
			RamTotalDisplay = "Unknown RAM";
		}
	}
}
