using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel.__Internals;
using CommunityToolkit.Mvvm.Input;
using QuantixLicensing;
using QuantixTweakingUtility.Services;

namespace QuantixTweakingUtility.ViewModels;

public sealed class LicenseViewModel : ObservableObject
{
	private readonly ILicenseService _licenseService;

	[ObservableProperty]
	private string _licenseKey = string.Empty;

	[ObservableProperty]
	private string _statusMessage;

	[ObservableProperty]
	private bool _hasError;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.2.0.0")]
	private AsyncRelayCommand? activateCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.2.0.0")]
	private RelayCommand? copyHardwareIdCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.2.0.0")]
	private RelayCommand? exitCommand;

	public string HardwareId { get; }

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.2.0.0")]
	[ExcludeFromCodeCoverage]
	public string LicenseKey
	{
		get
		{
			return _licenseKey;
		}
		[MemberNotNull("_licenseKey")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_licenseKey, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.LicenseKey);
				_licenseKey = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.LicenseKey);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.2.0.0")]
	[ExcludeFromCodeCoverage]
	public string StatusMessage
	{
		get
		{
			return _statusMessage;
		}
		[MemberNotNull("_statusMessage")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_statusMessage, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.StatusMessage);
				_statusMessage = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.StatusMessage);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.2.0.0")]
	[ExcludeFromCodeCoverage]
	public bool HasError
	{
		get
		{
			return _hasError;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_hasError, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.HasError);
				_hasError = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.HasError);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.2.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand ActivateCommand => activateCommand ?? (activateCommand = new AsyncRelayCommand(ActivateAsync));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.2.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand CopyHardwareIdCommand => copyHardwareIdCommand ?? (copyHardwareIdCommand = new RelayCommand(CopyHardwareId));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.2.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand ExitCommand => exitCommand ?? (exitCommand = new RelayCommand(Exit));

	public event EventHandler? LicenseAccepted;

	public event EventHandler? ExitRequested;

	public LicenseViewModel(ILicenseService licenseService)
	{
		_licenseService = licenseService;
		HardwareId = licenseService.FormattedHardwareId;
		_licenseKey = licenseService.ReadSavedToken() ?? string.Empty;
		_statusMessage = (string.IsNullOrWhiteSpace(_licenseKey) ? "Enter your Quantix Elite key. Activation is saved on this PC." : "Saved key found. Quantix will verify this PC automatically.");
		_hasError = false;
	}

	[RelayCommand]
	private async Task ActivateAsync()
	{
		StatusMessage = "Verifying license for this PC...";
		HasError = false;
		LicenseValidationResult licenseValidationResult = await _licenseService.ActivateAsync(LicenseKey);
		StatusMessage = licenseValidationResult.Message;
		HasError = !licenseValidationResult.IsValid;
		if (licenseValidationResult.IsValid)
		{
			this.LicenseAccepted?.Invoke(this, EventArgs.Empty);
		}
	}

	[RelayCommand]
	private void CopyHardwareId()
	{
		try
		{
			Clipboard.SetText(HardwareId);
			StatusMessage = "HWID copied. Send it to Quantix to receive a key for this PC.";
			HasError = false;
		}
		catch
		{
			StatusMessage = "Windows could not copy the HWID. Select it and press Ctrl+C.";
			HasError = true;
		}
	}

	[RelayCommand]
	private void Exit()
	{
		this.ExitRequested?.Invoke(this, EventArgs.Empty);
	}
}
