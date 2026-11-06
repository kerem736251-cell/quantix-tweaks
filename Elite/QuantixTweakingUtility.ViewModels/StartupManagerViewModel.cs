using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel.__Internals;
using CommunityToolkit.Mvvm.Input;
using QuantixTweakingUtility.Models;
using QuantixTweakingUtility.Services;

namespace QuantixTweakingUtility.ViewModels;

public sealed class StartupManagerViewModel : ObservableObject
{
	private readonly IStartupManagerService _startupManager;

	[ObservableProperty]
	private bool _isLoading;

	[ObservableProperty]
	private string _statusMessage = "Not scanned yet.";

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.2.0.0")]
	private AsyncRelayCommand? refreshCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.2.0.0")]
	private AsyncRelayCommand<StartupRowViewModel?>? toggleCommand;

	public ObservableCollection<StartupRowViewModel> Items { get; } = new ObservableCollection<StartupRowViewModel>();

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.2.0.0")]
	[ExcludeFromCodeCoverage]
	public bool IsLoading
	{
		get
		{
			return _isLoading;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_isLoading, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.IsLoading);
				_isLoading = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.IsLoading);
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
	public IAsyncRelayCommand RefreshCommand => refreshCommand ?? (refreshCommand = new AsyncRelayCommand(RefreshAsync));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.2.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand<StartupRowViewModel?> ToggleCommand => toggleCommand ?? (toggleCommand = new AsyncRelayCommand<StartupRowViewModel>(ToggleAsync));

	public StartupManagerViewModel(IStartupManagerService startupManager)
	{
		_startupManager = startupManager;
		RefreshAsync();
	}

	[RelayCommand]
	private async Task RefreshAsync()
	{
		if (IsLoading)
		{
			return;
		}
		IsLoading = true;
		StatusMessage = "Reading startup items...";
		try
		{
			List<StartupItem> list = await _startupManager.GetStartupItemsAsync();
			Items.Clear();
			foreach (StartupItem item in list)
			{
				Items.Add(new StartupRowViewModel(item));
			}
			StatusMessage = $"{list.Count} startup items found.";
		}
		catch (Exception)
		{
			StatusMessage = "Could not read startup items - see the log for details.";
		}
		finally
		{
			IsLoading = false;
		}
	}

	[RelayCommand]
	private async Task ToggleAsync(StartupRowViewModel? row)
	{
		if (row == null || row.IsBusy)
		{
			return;
		}
		row.IsBusy = true;
		try
		{
			TweakActionResult tweakActionResult = (row.Info.IsEnabled ? (await _startupManager.DisableAsync(row.Info)) : (await _startupManager.EnableAsync(row.Info)));
			TweakActionResult tweakActionResult2 = tweakActionResult;
			StatusMessage = tweakActionResult2.Message;
			row.IsBusy = false;
			if (tweakActionResult2.Success)
			{
				await RefreshAsync();
			}
		}
		catch (Exception ex)
		{
			StatusMessage = "Startup change failed: " + ex.Message;
		}
		finally
		{
			row.IsBusy = false;
		}
	}
}
