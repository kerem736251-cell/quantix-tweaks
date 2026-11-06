using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using System.Threading;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel.__Internals;
using CommunityToolkit.Mvvm.Input;
using QuantixTweakingUtility.Models;
using QuantixTweakingUtility.Services;

namespace QuantixTweakingUtility.ViewModels;

public sealed class TweakRowViewModel : ObservableObject
{
	private static readonly SemaphoreSlim InitialReads = new(3, 3);
	private readonly ITweakService _tweaks;

	private TweakState _state;
	private bool _hasReadState;
	private bool _requiresElevation;
	private int _readVersion;

	[ObservableProperty]
	private bool _isApplied;

	[ObservableProperty]
	private bool _isBusy;

	[ObservableProperty]
	private string _busyActionText = string.Empty;

	[ObservableProperty]
	private string _statusText = string.Empty;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.2.0.0")]
	private AsyncRelayCommand? toggleCommand;

	public string StateLabel => !_hasReadState ? "CHECKING…" : _requiresElevation ? "ADMIN REQUIRED" : Definition.IsOneTimeAction && _state == TweakState.NotApplied ? "READY" : _state switch
	{
		TweakState.Applied => "ON", 
		TweakState.NotApplied => "OFF", 
		TweakState.Unsupported => "UNAVAILABLE", 
		_ => "UNKNOWN", 
	};

	public TweakDefinition Definition { get; }
	public Task Initialization { get; }

	public bool CanSelect => CanToggle();

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.2.0.0")]
	[ExcludeFromCodeCoverage]
	public bool IsApplied
	{
		get
		{
			return _isApplied;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_isApplied, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.IsApplied);
				_isApplied = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.IsApplied);
			}
		}
	}

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
				ToggleCommand.NotifyCanExecuteChanged();
		OnPropertyChanged(nameof(CanSelect));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.2.0.0")]
	[ExcludeFromCodeCoverage]
	public string BusyActionText
	{
		get
		{
			return _busyActionText;
		}
		[MemberNotNull("_busyActionText")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_busyActionText, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.BusyActionText);
				_busyActionText = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.BusyActionText);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.2.0.0")]
	[ExcludeFromCodeCoverage]
	public string StatusText
	{
		get
		{
			return _statusText;
		}
		[MemberNotNull("_statusText")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_statusText, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.StatusText);
				_statusText = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.StatusText);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.2.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand ToggleCommand => toggleCommand ?? (toggleCommand = new AsyncRelayCommand(ToggleAsync, CanToggle));

	private bool CanToggle()
	{
		if (!IsBusy && (!Definition.RestoreOnly || IsApplied))
		{
			if (_state != TweakState.Applied)
			{
				return _state == TweakState.NotApplied;
			}
			return true;
		}
		return false;
	}

	public TweakRowViewModel(TweakDefinition definition, ITweakService tweaks)
	{
		Definition = definition;
		_tweaks = tweaks;
		Initialization = InitializeStateAsync();
	}

	private async Task InitializeStateAsync()
	{
		await InitialReads.WaitAsync();
		try { await RefreshStateAsync(); }
		finally { InitialReads.Release(); }
	}

	public async Task RefreshStateAsync()
	{
		int version = Interlocked.Increment(ref _readVersion);
		try
		{
			var state = await Definition.CurrentStateReader();
			if (version != Volatile.Read(ref _readVersion)) return;
			_state = state;
			_requiresElevation = false;
			IsApplied = _state == TweakState.Applied;
			StatusText = _state switch
			{
				TweakState.Unsupported => "Not supported on this system. No changes will be made.", 
				TweakState.Unknown => "State unavailable. Refresh status before changing this setting.", 
				TweakState.Applied => "Currently applied.", 
				_ => "Not applied.", 
			};
		}
		catch (UnauthorizedAccessException)
		{
			if (version != Volatile.Read(ref _readVersion)) return;
			_state = TweakState.Unknown;
			_requiresElevation = true;
			StatusText = "Launch Elite as administrator to read this setting. No changes have been made.";
		}
		catch
		{
			if (version != Volatile.Read(ref _readVersion)) return;
			_state = TweakState.Unknown;
			_requiresElevation = false;
			StatusText = "State unavailable. Refresh status before changing this setting.";
		}
		if (Definition.RestoreOnly)
		{
			StatusText = (IsApplied ? "Retired for stability. Switch OFF to restore your saved values, then restart Windows." : "Retired for stability. New application is blocked; no saved recovery is available.");
		}
		_hasReadState = true;
		OnPropertyChanged("StateLabel");
		ToggleCommand.NotifyCanExecuteChanged();
		OnPropertyChanged(nameof(CanSelect));
	}

	[RelayCommand]
	private Task ToggleAsync() => ChangeAsync(null);

	internal async Task ChangeAsync(bool? apply)
	{
		if (Definition.IsOneTimeAction && apply == false)
		{
			StatusText = "This is a one-time action; there is no saved setting to restore.";
			return;
		}
		if (IsBusy)
		{
			return;
		}
		IsBusy = true;
		await RefreshStateAsync();
		IsBusy = false;
		bool hasOriginal = false;
		if (apply == false && !IsApplied)
		{
			try { hasOriginal = Definition.HasSavedOriginal?.Invoke() == true; }
			catch (Exception ex)
			{
				StatusText = "Could not read the saved original: " + ex.Message;
				return;
			}
		}
		if (!CanToggle() || (apply == true && IsApplied) || (apply == false && !IsApplied && !hasOriginal))
		{
			return;
		}
		bool restoring = apply == false || (apply == null && IsApplied);
		if (!restoring && Definition.RiskLevel == TweakRiskLevel.High && !(await App.ConfirmTweakAsync(Definition.Name, Definition.Description)))
		{
			StatusText = "High-risk change cancelled.";
			return;
		}
		bool wasApplied = restoring;
		BusyActionText = (wasApplied ? "RESTORING" : "APPLYING");
		StatusText = (wasApplied ? ("Restoring " + Definition.Name + "...") : ("Applying " + Definition.Name + "..."));
		IsBusy = true;
		try
		{
			TweakActionResult? result = null;
			async Task ChangeSettingAsync()
			{
				result = wasApplied
					? await _tweaks.RestoreTweakAsync(Definition.Id)
					: await _tweaks.ApplyTweakAsync(Definition.Id);
			}
			if (apply.HasValue)
				await ChangeSettingAsync();
			else
				await App.ShowProgressAsync(wasApplied ? "Restoring tweak" : "Applying tweak",
					Definition.Name, ChangeSettingAsync, Application.Current.MainWindow);
			TweakActionResult tweakActionResult3 = result!;
			await RefreshStateAsync();
			string text = ((_state == TweakState.Unknown || _state == TweakState.Unsupported) ? (" " + StatusText) : "");
			StatusText = tweakActionResult3.Message + text;
			if (tweakActionResult3.Success && Definition.RequiresRestart)
			{
				StatusText += " Restart Windows to finish this change.";
			}
		}
		catch (Exception ex)
		{
			await RefreshStateAsync();
			StatusText = "Could not change " + Definition.Name + ": " + ex.Message;
		}
		finally
		{
			IsBusy = false;
			BusyActionText = string.Empty;
			OnPropertyChanged("IsApplied");
		}
	}
}




