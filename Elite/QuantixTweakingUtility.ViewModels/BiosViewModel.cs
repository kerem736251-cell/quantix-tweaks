using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel.__Internals;
using CommunityToolkit.Mvvm.Input;
using QuantixTweakingUtility.Services;

namespace QuantixTweakingUtility.ViewModels;

public sealed class BiosViewModel : ObservableObject
{
	private AsyncRelayCommand? scanCommand;

	private RelayCommand? detectedSupportCommand;

	[ObservableProperty]
	private string _selectedBrand = "ASUS";

	[ObservableProperty]
	private string _selectedPlatform = "Select your CPU platform";

	[ObservableProperty]
	private string _motherboardModel = string.Empty;

	[ObservableProperty]
	private string _cpuModel = string.Empty;

	[ObservableProperty]
	private bool _includeMemoryProfile;

	[ObservableProperty]
	private bool _pboSupportConfirmed;

	[ObservableProperty]
	private bool _includeCStateExperiment;

	[ObservableProperty]
	private string _exportStatus = "Save a PNG to keep this checklist on your phone while you are in BIOS.";

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.2.0.0")]
	private RelayCommand? openSupportCommand;

	public FirmwareSnapshot? ScanResult { get; private set; }

	public IReadOnlyList<FirmwareReview> Reviews { get; private set; } = Array.Empty<FirmwareReview>();

	public string ScanStatus { get; private set; } = "Ready to scan. No settings will be changed.";

	public string ScanIssues
	{
		get
		{
			if (ScanResult != null)
			{
				return string.Join("\n", ScanResult.Issues);
			}
			return "";
		}
	}

	public string ScanReport
	{
		get
		{
			if (ScanResult != null)
			{
				return $"QUANTIX ELITE / HARDWARE SCAN\n{ScanResult.Manufacturer}\n{ScanResult.Board}\n{ScanResult.Cpu}\nBIOS: {ScanResult.Bios}\n{ScanResult.Memory}\n{ScanResult.MemoryDetails}\n\n{ScanResult.SupportStatus}\n{ScanIssues}";
			}
			return ScanStatus;
		}
	}

	public IAsyncRelayCommand ScanCommand => scanCommand ?? (scanCommand = new AsyncRelayCommand(ScanAsync));

	public IRelayCommand DetectedSupportCommand => detectedSupportCommand ?? (detectedSupportCommand = new RelayCommand(delegate
	{
		try
		{
			Process.Start(new ProcessStartInfo(ScanResult.SupportUrl)
			{
				UseShellExecute = true
			});
		}
		catch
		{
			ScanStatus = "Could not open the manufacturer's support page.";
			OnPropertyChanged("ScanStatus");
		}
	}, () => !string.IsNullOrEmpty(ScanResult?.SupportUrl)));

	public IReadOnlyList<string> MotherboardBrands { get; } = new string[5] { "ASUS", "MSI", "Gigabyte", "ASRock", "Other / OEM / laptop" };

	public IReadOnlyList<string> CpuPlatforms { get; } = BiosGuideCatalog.Platforms;

	public string GuideText => BuildGuide(SelectedBrand, SelectedPlatform, MotherboardModel, CpuModel, IncludeMemoryProfile, PboSupportConfirmed, IncludeCStateExperiment);

	public bool IsAmd => SelectedPlatform.StartsWith("AMD");

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.2.0.0")]
	[ExcludeFromCodeCoverage]
	public string SelectedBrand
	{
		get
		{
			return _selectedBrand;
		}
		[MemberNotNull("_selectedBrand")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_selectedBrand, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.SelectedBrand);
				_selectedBrand = value;
				OnSelectedBrandChanged(value);
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.SelectedBrand);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.2.0.0")]
	[ExcludeFromCodeCoverage]
	public string SelectedPlatform
	{
		get
		{
			return _selectedPlatform;
		}
		[MemberNotNull("_selectedPlatform")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_selectedPlatform, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.SelectedPlatform);
				_selectedPlatform = value;
				OnSelectedPlatformChanged(value);
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.SelectedPlatform);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.2.0.0")]
	[ExcludeFromCodeCoverage]
	public string MotherboardModel
	{
		get
		{
			return _motherboardModel;
		}
		[MemberNotNull("_motherboardModel")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_motherboardModel, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.MotherboardModel);
				_motherboardModel = value;
				OnMotherboardModelChanged(value);
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.MotherboardModel);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.2.0.0")]
	[ExcludeFromCodeCoverage]
	public string CpuModel
	{
		get
		{
			return _cpuModel;
		}
		[MemberNotNull("_cpuModel")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_cpuModel, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.CpuModel);
				_cpuModel = value;
				OnCpuModelChanged(value);
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.CpuModel);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.2.0.0")]
	[ExcludeFromCodeCoverage]
	public bool IncludeMemoryProfile
	{
		get
		{
			return _includeMemoryProfile;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_includeMemoryProfile, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.IncludeMemoryProfile);
				_includeMemoryProfile = value;
				OnIncludeMemoryProfileChanged(value);
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.IncludeMemoryProfile);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.2.0.0")]
	[ExcludeFromCodeCoverage]
	public bool PboSupportConfirmed
	{
		get
		{
			return _pboSupportConfirmed;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_pboSupportConfirmed, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.PboSupportConfirmed);
				_pboSupportConfirmed = value;
				OnPboSupportConfirmedChanged(value);
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.PboSupportConfirmed);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.2.0.0")]
	[ExcludeFromCodeCoverage]
	public bool IncludeCStateExperiment
	{
		get
		{
			return _includeCStateExperiment;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_includeCStateExperiment, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.IncludeCStateExperiment);
				_includeCStateExperiment = value;
				OnIncludeCStateExperimentChanged(value);
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.IncludeCStateExperiment);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.2.0.0")]
	[ExcludeFromCodeCoverage]
	public string ExportStatus
	{
		get
		{
			return _exportStatus;
		}
		[MemberNotNull("_exportStatus")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_exportStatus, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.ExportStatus);
				_exportStatus = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.ExportStatus);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.2.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand OpenSupportCommand => openSupportCommand ?? (openSupportCommand = new RelayCommand(OpenSupport));

	public async Task ScanAsync()
	{
		ScanStatus = "Detecting CPU, memory, motherboard and BIOS...";
		OnPropertyChanged("ScanStatus");
		try
		{
			ScanResult = await Task.Run((Func<FirmwareSnapshot>)FirmwareScanner.Scan);
			Reviews = FirmwareScanner.Review(ScanResult);
			ScanStatus = ((ScanResult.Issues.Count == 0) ? "Scan complete. No settings changed." : "Partial scan. Some firmware inventory was unavailable.");
		}
		catch
		{
			ScanResult = null;
			Reviews = Array.Empty<FirmwareReview>();
			ScanStatus = "Scan failed. No settings changed. You can try again.";
		}
		OnPropertyChanged("ScanResult");
		OnPropertyChanged("Reviews");
		OnPropertyChanged("ScanStatus");
		OnPropertyChanged("ScanIssues");
		OnPropertyChanged("ScanReport");
		DetectedSupportCommand.NotifyCanExecuteChanged();
	}

	private void RefreshGuide()
	{
		OnPropertyChanged("GuideText");
	}

	public static string BuildGuide(string brand, string platform, string board, string cpu, bool memoryProfile, bool pboConfirmed = false, bool cStateExperiment = false)
	{
		return BiosGuideCatalog.Build(brand, platform, board, cpu, memoryProfile, pboConfirmed, cStateExperiment);
	}

	public static string GetSupportUrl(string brand)
	{
		return brand switch
		{
			"ASUS" => "https://www.asus.com/support/", 
			"MSI" => "https://www.msi.com/support", 
			"Gigabyte" => "https://www.gigabyte.com/Support", 
			"ASRock" => "https://www.asrock.com/support/", 
			_ => "https://support.microsoft.com/windows", 
		};
	}

	[RelayCommand]
	private void OpenSupport()
	{
		try
		{
			Process.Start(new ProcessStartInfo(GetSupportUrl(SelectedBrand))
			{
				UseShellExecute = true
			});
		}
		catch (Exception ex)
		{
			ExportStatus = "Could not open the support page: " + ex.Message;
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.2.0.0")]
	private void OnSelectedBrandChanged(string value)
	{
		PboSupportConfirmed = false;
		RefreshGuide();
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.2.0.0")]
	private void OnSelectedPlatformChanged(string value)
	{
		PboSupportConfirmed = false;
		IncludeCStateExperiment = false;
		OnPropertyChanged("IsAmd");
		RefreshGuide();
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.2.0.0")]
	private void OnMotherboardModelChanged(string value)
	{
		PboSupportConfirmed = false;
		RefreshGuide();
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.2.0.0")]
	private void OnCpuModelChanged(string value)
	{
		PboSupportConfirmed = false;
		RefreshGuide();
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.2.0.0")]
	private void OnIncludeMemoryProfileChanged(bool value)
	{
		RefreshGuide();
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.2.0.0")]
	private void OnPboSupportConfirmedChanged(bool value)
	{
		RefreshGuide();
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.2.0.0")]
	private void OnIncludeCStateExperimentChanged(bool value)
	{
		RefreshGuide();
	}
}
