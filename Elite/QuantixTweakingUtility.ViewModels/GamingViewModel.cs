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

public sealed class GamingViewModel : ObservableObject
{
	private static readonly IReadOnlyList<string> GameOptimizationTweakIds = new string[13]
	{
		"cpu.power-plan.quantix-elite", "perf.power-throttling.disable", "gaming.hags.enable", "memory.compression.disable", "perf.usb-selective-suspend", "gaming.mouse-acceleration.disable", "input.keyboard-response.fast", "perf.background-apps.disable", "gaming.game-mode.enable", "gaming.game-bar-capture.disable",
		"gaming.mmcss-priority.boost", "network.throttling-index.disable", "windows.audio-ducking.disable"
	};

	private readonly ITweakService _tweaks;

	private readonly IGameLibraryService _gameLibrary;

	[ObservableProperty]
	private bool _isScanning;

	[ObservableProperty]
	private string _scanStatus = "Not scanned yet.";

	[ObservableProperty]
	private bool _isOptimizingGame;

	[ObservableProperty]
	private string _gameOptimizeStatus = string.Empty;

	[ObservableProperty]
	private string _gameOptimizationStep = string.Empty;

	[ObservableProperty]
	private double _gameOptimizationProgress;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.2.0.0")]
	private AsyncRelayCommand<DetectedGame?>? optimizeGameCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.2.0.0")]
	private AsyncRelayCommand? scanForGamesCommand;

	public ObservableCollection<TweakRowViewModel> Tweaks { get; }

	public ObservableCollection<DetectedGame> DetectedGames { get; } = new ObservableCollection<DetectedGame>();

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
	public string ScanStatus
	{
		get
		{
			return _scanStatus;
		}
		[MemberNotNull("_scanStatus")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_scanStatus, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.ScanStatus);
				_scanStatus = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.ScanStatus);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.2.0.0")]
	[ExcludeFromCodeCoverage]
	public bool IsOptimizingGame
	{
		get
		{
			return _isOptimizingGame;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_isOptimizingGame, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.IsOptimizingGame);
				_isOptimizingGame = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.IsOptimizingGame);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.2.0.0")]
	[ExcludeFromCodeCoverage]
	public string GameOptimizeStatus
	{
		get
		{
			return _gameOptimizeStatus;
		}
		[MemberNotNull("_gameOptimizeStatus")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_gameOptimizeStatus, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.GameOptimizeStatus);
				_gameOptimizeStatus = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.GameOptimizeStatus);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.2.0.0")]
	[ExcludeFromCodeCoverage]
	public string GameOptimizationStep
	{
		get
		{
			return _gameOptimizationStep;
		}
		[MemberNotNull("_gameOptimizationStep")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_gameOptimizationStep, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.GameOptimizationStep);
				_gameOptimizationStep = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.GameOptimizationStep);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.2.0.0")]
	[ExcludeFromCodeCoverage]
	public double GameOptimizationProgress
	{
		get
		{
			return _gameOptimizationProgress;
		}
		set
		{
			if (!EqualityComparer<double>.Default.Equals(_gameOptimizationProgress, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.GameOptimizationProgress);
				_gameOptimizationProgress = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.GameOptimizationProgress);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.2.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand<DetectedGame?> OptimizeGameCommand => optimizeGameCommand ?? (optimizeGameCommand = new AsyncRelayCommand<DetectedGame>(OptimizeGameAsync));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.2.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand ScanForGamesCommand => scanForGamesCommand ?? (scanForGamesCommand = new AsyncRelayCommand(ScanForGamesAsync));

	public GamingViewModel(ITweakService tweaks, IGameLibraryService gameLibrary)
	{
		_tweaks = tweaks;
		_gameLibrary = gameLibrary;
		Tweaks = new ObservableCollection<TweakRowViewModel>(from tweak in tweaks.GetTweaksByCategory(TweakCategory.Gaming)
			select new TweakRowViewModel(tweak, tweaks));
		ScanForGamesAsync();
	}

	[RelayCommand]
	private async Task OptimizeGameAsync(DetectedGame? game)
	{
		if (IsOptimizingGame)
		{
			return;
		}
		string label = game?.Name ?? "your games";
		IsOptimizingGame = true;
		GameOptimizationProgress = 0.0;
		GameOptimizationStep = "Checking the preset for " + label + "...";
		GameOptimizeStatus = GameOptimizationStep;
		try
		{
			Dictionary<string, TweakDefinition> catalog = _tweaks.GetAllTweaks().ToDictionary<TweakDefinition, string>((TweakDefinition tweakDefinition) => tweakDefinition.Id, StringComparer.OrdinalIgnoreCase);
			List<TweakDefinition> preset = (from id in GameOptimizationTweakIds.Where(catalog.ContainsKey)
				select catalog[id]).ToList();
			List<TweakDefinition> pending = new List<TweakDefinition>();
			List<TweakDefinition> alreadyActive = new List<TweakDefinition>();
			List<TweakDefinition> unsupported = new List<TweakDefinition>();
			for (int index = 0; index < preset.Count; index++)
			{
				TweakDefinition tweak = preset[index];
				GameOptimizationStep = $"Checking {index + 1} of {preset.Count}: {tweak.Name}";
				TweakState tweakState = await tweak.CurrentStateReader();
				GameOptimizationProgress = ((double)index + 1.0) / (double)preset.Count * 100.0;
				switch (tweakState)
				{
				case TweakState.Applied:
					alreadyActive.Add(tweak);
					break;
				case TweakState.Unsupported:
					unsupported.Add(tweak);
					break;
				case TweakState.NotApplied:
					pending.Add(tweak);
					break;
				default:
					unsupported.Add(tweak);
					break;
				}
			}
			IsOptimizingGame = false;
			if (MessageBox.Show(BuildGameOptimizationReview(label, pending, alreadyActive, unsupported), "Review optimization for " + label, MessageBoxButton.OKCancel, MessageBoxImage.Asterisk) != MessageBoxResult.OK)
			{
				GameOptimizeStatus = "Optimization cancelled for " + label + ". No settings were changed.";
				return;
			}
			IsOptimizingGame = true;
			GameOptimizationProgress = 0.0;
			GameOptimizationStep = ((pending.Count == 0) ? "All supported preset settings are already active." : $"Preparing {pending.Count} Windows optimization(s) for {label}...");
			GameOptimizeStatus = GameOptimizationStep;
			List<TweakDefinition> applied = new List<TweakDefinition>();
			List<(TweakDefinition Tweak, string Message)> failed = new List<(TweakDefinition, string)>();
			for (int index = 0; index < pending.Count; index++)
			{
				TweakDefinition tweak = pending[index];
				GameOptimizationStep = $"Applying {index + 1} of {pending.Count}: {tweak.Name}";
				GameOptimizeStatus = GameOptimizationStep;
				await Task.Yield();
				TweakActionResult tweakActionResult = await _tweaks.ApplyTweakAsync(tweak.Id);
				GameOptimizationProgress = ((double)index + 1.0) / (double)pending.Count * 100.0;
				if (tweakActionResult.Success)
				{
					applied.Add(tweak);
				}
				else
				{
					failed.Add((tweak, tweakActionResult.Message));
				}
			}
			GameOptimizationProgress = 100.0;
			GameOptimizationStep = "Refreshing tweak states and preparing the report...";
			foreach (TweakRowViewModel tweak2 in Tweaks)
			{
				await tweak2.RefreshStateAsync();
			}
			GameOptimizeStatus = ((failed.Count != 0) ? $"Optimized {label} with {applied.Count} applied and {failed.Count} failed setting(s)." : ((applied.Count == 0) ? ("Optimized " + label + " â€” every supported preset setting was already active.") : $"Optimized {label} â€” applied {applied.Count} setting(s): {string.Join(", ", applied.Select((TweakDefinition tweakDefinition) => tweakDefinition.Name))}."));
			MessageBox.Show(BuildGameOptimizationResult(label, applied, alreadyActive, unsupported, failed), "Optimization results — " + label, MessageBoxButton.OK, (failed.Count == 0) ? MessageBoxImage.Asterisk : MessageBoxImage.Exclamation);
		}
		catch (Exception ex)
		{
			GameOptimizeStatus = "Optimization failed for " + label + " â€” see the log for details.";
			MessageBox.Show("Optimization could not be completed for " + label + ".\n\n" + ex.Message, "Optimization failed â€” " + label, MessageBoxButton.OK, MessageBoxImage.Hand);
		}
		finally
		{
			IsOptimizingGame = false;
			GameOptimizationStep = string.Empty;
		}
	}

	private static string BuildGameOptimizationReview(string gameName, IReadOnlyCollection<TweakDefinition> pending, IReadOnlyCollection<TweakDefinition> alreadyActive, IReadOnlyCollection<TweakDefinition> unsupported)
	{
		List<string> list = new List<string>
		{
			"Quantix will apply this Windows-level preset for " + gameName + ".",
			"These Windows settings affect all games; results vary. Game files are not edited.",
			string.Empty,
			$"WILL APPLY ({pending.Count})"
		};
		list.AddRange(pending.Select(FormatTweakLine));
		if (pending.Count == 0)
		{
			list.Add("â€¢ Nothing currently needs applying, or its state could not be verified.");
		}
		if (alreadyActive.Count > 0)
		{
			list.Add(string.Empty);
			list.Add($"ALREADY ACTIVE ({alreadyActive.Count})");
			list.AddRange(alreadyActive.Select(FormatTweakLine));
		}
		if (unsupported.Count > 0)
		{
			list.Add(string.Empty);
			list.Add($"UNAVAILABLE ON THIS PC ({unsupported.Count})");
			list.AddRange(unsupported.Select(FormatTweakLine));
		}
		list.Add(string.Empty);
		list.Add("Click OK to apply exactly this list, or Cancel to leave everything unchanged.");
		return string.Join(Environment.NewLine, list);
	}

	private static string BuildGameOptimizationResult(string gameName, IReadOnlyCollection<TweakDefinition> applied, IReadOnlyCollection<TweakDefinition> alreadyActive, IReadOnlyCollection<TweakDefinition> unsupported, IReadOnlyCollection<(TweakDefinition Tweak, string Message)> failed)
	{
		List<string> list = new List<string>
		{
			"Optimization results for " + gameName,
			string.Empty,
			$"ENABLED / APPLIED NOW ({applied.Count})"
		};
		list.AddRange(applied.Select(FormatTweakLine));
		if (applied.Count == 0)
		{
			list.Add("â€¢ No new changes were needed.");
		}
		if (alreadyActive.Count > 0)
		{
			list.Add(string.Empty);
			list.Add($"ALREADY ACTIVE ({alreadyActive.Count})");
			list.AddRange(alreadyActive.Select(FormatTweakLine));
		}
		if (unsupported.Count > 0)
		{
			list.Add(string.Empty);
			list.Add($"UNAVAILABLE ON THIS PC ({unsupported.Count})");
			list.AddRange(unsupported.Select(FormatTweakLine));
		}
		if (failed.Count > 0)
		{
			list.Add(string.Empty);
			list.Add($"FAILED ({failed.Count})");
			list.AddRange(failed.Select(((TweakDefinition Tweak, string Message) item) => "â€¢ " + item.Tweak.Name + ": " + item.Message));
		}
		if (applied.Any((TweakDefinition tweak) => tweak.RequiresRestart))
		{
			list.Add(string.Empty);
			list.Add("Restart Windows to finish the settings marked [restart].");
		}
		return string.Join(Environment.NewLine, list);
	}

	private static string FormatTweakLine(TweakDefinition tweak)
	{
		string text = (tweak.RequiresRestart ? " [restart]" : string.Empty);
		return "â€¢ " + tweak.Name + text;
	}

	[RelayCommand]
	private async Task ScanForGamesAsync()
	{
		if (IsScanning)
		{
			return;
		}
		IsScanning = true;
		ScanStatus = "Scanning Steam and Epic Games libraries...";
		try
		{
			List<DetectedGame> list = await _gameLibrary.ScanForInstalledGamesAsync();
			DetectedGames.Clear();
			foreach (DetectedGame item in list)
			{
				DetectedGames.Add(item);
			}
			ScanStatus = list.Count switch
			{
				0 => "No Steam or Epic Games installations were found on this PC.", 
				1 => "Found 1 installed game.", 
				_ => $"Found {list.Count} installed games.", 
			};
		}
		catch
		{
			ScanStatus = "Scan failed - see the log for details.";
		}
		finally
		{
			IsScanning = false;
		}
	}
}
