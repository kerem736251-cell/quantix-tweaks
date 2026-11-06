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

public sealed class PerformanceViewModel : ObservableObject
{
	private readonly ITweakService _tweakService;

	private readonly IAppSettingsService _settings;

	private readonly ILoggingService _logging;

	private readonly IReadOnlyList<TweakRowViewModel> _allRows;

	[ObservableProperty]
	private bool _isOptimizing;

	[ObservableProperty]
	private double _optimizationProgress;

	[ObservableProperty]
	private string _statusMessage = "Choose tweaks individually or apply the safe recommendations together.";

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.2.0.0")]
	private AsyncRelayCommand? optimizeSystemCommand;

	public ObservableCollection<TweakRowViewModel> CpuTweaks { get; }

	public ObservableCollection<TweakRowViewModel> GpuTweaks { get; }

	public ObservableCollection<TweakRowViewModel> MemoryTweaks { get; }

	public ObservableCollection<TweakRowViewModel> InputTweaks { get; }

	public ObservableCollection<TweakRowViewModel> StorageTweaks { get; }

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.2.0.0")]
	[ExcludeFromCodeCoverage]
	public bool IsOptimizing
	{
		get
		{
			return _isOptimizing;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_isOptimizing, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.IsOptimizing);
				_isOptimizing = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.IsOptimizing);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.2.0.0")]
	[ExcludeFromCodeCoverage]
	public double OptimizationProgress
	{
		get
		{
			return _optimizationProgress;
		}
		set
		{
			if (!EqualityComparer<double>.Default.Equals(_optimizationProgress, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.OptimizationProgress);
				_optimizationProgress = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.OptimizationProgress);
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
	public IAsyncRelayCommand OptimizeSystemCommand => optimizeSystemCommand ?? (optimizeSystemCommand = new AsyncRelayCommand(OptimizeSystemAsync));

	public PerformanceViewModel(ITweakService tweakService, IAppSettingsService settings, ILoggingService logging)
	{
		_tweakService = tweakService;
		_settings = settings;
		_logging = logging;
		CpuTweaks = BuildRows(TweakCategory.Cpu);
		GpuTweaks = BuildRows(TweakCategory.Gpu);
		MemoryTweaks = BuildRows(TweakCategory.Memory);
		InputTweaks = BuildRows(TweakCategory.Input);
		StorageTweaks = BuildRows(TweakCategory.Storage);
		_allRows = CpuTweaks.Concat(GpuTweaks).Concat(MemoryTweaks).Concat(InputTweaks)
			.Concat(StorageTweaks)
			.ToList();
	}

	private ObservableCollection<TweakRowViewModel> BuildRows(TweakCategory category)
	{
		return new ObservableCollection<TweakRowViewModel>(from tweak in _tweakService.GetTweaksByCategory(category)
			select new TweakRowViewModel(tweak, _tweakService));
	}

	[RelayCommand]
	private async Task OptimizeSystemAsync()
	{
		if (IsOptimizing)
		{
			return;
		}
		List<TweakDefinition> pending;
		try
		{
			pending = await _tweakService.GetRecommendedTweaksAsync();
		}
		catch (Exception ex)
		{
			_logging.Error("SystemTweaks", "Could not assess recommended tweak state.", ex);
			StatusMessage = "Quantix could not assess the recommended settings.";
			return;
		}
		if (pending.Count == 0)
		{
			StatusMessage = "No pending low-risk recommendations were found. Unsupported or unreadable settings are skipped.";
			return;
		}
		if (_settings.Settings.ConfirmBeforeOptimizing)
		{
			string text = string.Join(Environment.NewLine, pending.Select((TweakDefinition tweakDefinition) => "  •  " + tweakDefinition.Name));
			if (MessageBox.Show("Quantix will apply these low-risk recommendations:\n\n" + text + "\n\nEvery setting is tracked for restore. Continue?", "Confirm Elite Optimization", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
			{
				return;
			}
		}
		IsOptimizing = true;
		OptimizationProgress = 0.0;
		try
		{
			List<string> failures = new List<string>();
			int applied = 0;
			bool restartNeeded = false;
			for (int index = 0; index < pending.Count; index++)
			{
				TweakDefinition tweak = pending[index];
				StatusMessage = "Applying " + tweak.Name + "...";
				TweakActionResult tweakActionResult = await _tweakService.ApplyTweakAsync(tweak.Id);
				OptimizationProgress = ((double)index + 1.0) / (double)pending.Count * 100.0;
				if (!tweakActionResult.Success)
				{
					failures.Add(tweak.Name + ": " + tweakActionResult.Message);
					continue;
				}
				applied++;
				restartNeeded |= tweak.RequiresRestart;
			}
			foreach (TweakRowViewModel allRow in _allRows)
			{
				await allRow.RefreshStateAsync();
			}
			StatusMessage = $"Optimization finished: {applied} applied, {failures.Count} failed." + (restartNeeded ? " Restart Windows to finish." : "") + ((failures.Count > 0) ? " Review the activity log for details." : "");
			if (failures.Count > 0)
			{
				_logging.Warn("SystemTweaks", "Some recommendations were skipped: " + string.Join(" | ", failures));
			}
		}
		catch (Exception ex2)
		{
			_logging.Error("SystemTweaks", "Optimization interrupted.", ex2);
			StatusMessage = "Optimization interrupted: " + ex2.Message + ". Earlier changes may already be applied; refresh their status before retrying.";
		}
		finally
		{
			IsOptimizing = false;
		}
	}
}
