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

public sealed class CleanupViewModel : ObservableObject
{
	private readonly ICleanupService _cleanup;

	[ObservableProperty]
	private bool _isScanning;

	[ObservableProperty]
	private bool _isCleaning;

	[ObservableProperty]
	private bool _hasScanned;

	[ObservableProperty]
	private string _statusMessage = "Scan only checks known cache folders; personal files are never included.";

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.2.0.0")]
	private AsyncRelayCommand? scanCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.2.0.0")]
	private AsyncRelayCommand? cleanSelectedCommand;

	public ObservableCollection<CleanupItemViewModel> Items { get; }

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.2.0.0")]
	[ExcludeFromCodeCoverage]
	public bool IsScanning
	{
		get
		{
			return _isScanning;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_isScanning, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.IsScanning);
				_isScanning = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.IsScanning);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.2.0.0")]
	[ExcludeFromCodeCoverage]
	public bool IsCleaning
	{
		get
		{
			return _isCleaning;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_isCleaning, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.IsCleaning);
				_isCleaning = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.IsCleaning);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.2.0.0")]
	[ExcludeFromCodeCoverage]
	public bool HasScanned
	{
		get
		{
			return _hasScanned;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_hasScanned, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.HasScanned);
				_hasScanned = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.HasScanned);
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

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.2.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand ScanCommand => scanCommand ?? (scanCommand = new AsyncRelayCommand(ScanAsync));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.2.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand CleanSelectedCommand => cleanSelectedCommand ?? (cleanSelectedCommand = new AsyncRelayCommand(CleanSelectedAsync));

	public CleanupViewModel(ICleanupService cleanup)
	{
		_cleanup = cleanup;
		Items = new ObservableCollection<CleanupItemViewModel>(from target in cleanup.GetTargets()
			select new CleanupItemViewModel(target));
	}

	[RelayCommand]
	private async Task ScanAsync()
	{
		if (IsScanning || IsCleaning)
		{
			return;
		}
		IsScanning = true;
		HasScanned = false;
		foreach (CleanupItemViewModel item in Items)
		{
			item.IsSelected = false;
			item.Status = "Scanning…";
			item.ReclaimableBytes = 0L;
		}
		StatusMessage = "Scanning safe cache locations...";
		try
		{
			IReadOnlyList<CleanupScanResult> readOnlyList = await _cleanup.ScanAsync();
			foreach (CleanupScanResult result in readOnlyList)
			{
				Items.First((CleanupItemViewModel item) => item.Target.Id == result.Target.Id).Update(result);
			}
			HasScanned = true;
			long num = readOnlyList.Sum((CleanupScanResult cleanupScanResult) => cleanupScanResult.ReclaimableBytes);
			StatusMessage = ((num == 0L) ? "No reclaimable files were found in the safe cache locations." : "Scan complete. Review the selected cache categories before cleaning.");
		}
		catch (Exception ex)
		{
			foreach (CleanupItemViewModel item2 in Items)
			{
				item2.Status = "Scan incomplete — scan again before cleaning.";
			}
			StatusMessage = "Scan could not complete: " + ex.Message;
		}
		finally
		{
			IsScanning = false;
		}
	}

	[RelayCommand]
	private async Task CleanSelectedAsync()
	{
		if (!HasScanned || IsCleaning || IsScanning)
		{
			return;
		}
		List<CleanupItemViewModel> list = Items.Where((CleanupItemViewModel item) => item.IsSelected && item.IsCleanable).ToList();
		if (list.Count == 0)
		{
			StatusMessage = "Choose one or more scanned cache categories first.";
			return;
		}
		string text = string.Join(Environment.NewLine, list.Select((CleanupItemViewModel item) => "  •  " + item.Target.Name));
		if (MessageBox.Show("Quantix will remove cache files from:\n\n" + text + "\n\nFiles in use are skipped. Personal folders, browser profiles, and the Recycle Bin are never touched. Continue?", "Confirm Safe Cleanup", MessageBoxButton.YesNo, MessageBoxImage.Exclamation) != MessageBoxResult.Yes)
		{
			return;
		}
		IsCleaning = true;
		StatusMessage = "Cleaning selected cache files...";
		try
		{
			CleanupActionResult result = await _cleanup.CleanAsync(list.Select((CleanupItemViewModel item) => item.Target.Id));
			StatusMessage = result.Message;
			if (result.Success)
			{
				IsCleaning = false;
				await ScanAsync();
				StatusMessage = result.Message + (HasScanned ? "" : " Follow-up scan failed; scan again before retrying.");
			}
			else
			{
				HasScanned = false;
				StatusMessage = result.Message + " Scan again before retrying.";
			}
		}
		catch (Exception ex)
		{
			HasScanned = false;
			StatusMessage = "Cleanup could not complete: " + ex.Message + " Scan again before retrying.";
		}
		finally
		{
			IsCleaning = false;
		}
	}
}
