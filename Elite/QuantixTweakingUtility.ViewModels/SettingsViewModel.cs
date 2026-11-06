using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.InteropServices;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel.__Internals;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using QuantixTweakingUtility.Services;

namespace QuantixTweakingUtility.ViewModels;

public sealed class SettingsViewModel : ObservableObject
{
	private readonly IAppSettingsService _settings;

	private readonly IRestoreService _restore;

	private readonly ILoggingService _logging;

	private bool _isLoading = true;

	private bool _revertingStartupSetting;

	private bool _revertingConfirmation;

	[ObservableProperty]
	private bool _launchAtSignIn;

	[ObservableProperty]
	private bool _confirmBeforeOptimizing;

	[ObservableProperty]
	private int _restorableTweakCount;

	[ObservableProperty]
	private string _statusMessage = "Settings are stored locally for this Windows account.";

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.2.0.0")]
	private RelayCommand? refreshBackupsCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.2.0.0")]
	private RelayCommand? openActivityLogCommand;

	public string BackupSummary { get; private set; } = "Refresh to list tracked changes.";

	public IRelayCommand ExportSupportSummaryCommand => new RelayCommand(ExportSupportSummary);

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.2.0.0")]
	[ExcludeFromCodeCoverage]
	public bool LaunchAtSignIn
	{
		get
		{
			return _launchAtSignIn;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_launchAtSignIn, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.LaunchAtSignIn);
				_launchAtSignIn = value;
				OnLaunchAtSignInChanged(value);
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.LaunchAtSignIn);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.2.0.0")]
	[ExcludeFromCodeCoverage]
	public bool ConfirmBeforeOptimizing
	{
		get
		{
			return _confirmBeforeOptimizing;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_confirmBeforeOptimizing, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.ConfirmBeforeOptimizing);
				_confirmBeforeOptimizing = value;
				OnConfirmBeforeOptimizingChanged(value);
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.ConfirmBeforeOptimizing);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.2.0.0")]
	[ExcludeFromCodeCoverage]
	public int RestorableTweakCount
	{
		get
		{
			return _restorableTweakCount;
		}
		set
		{
			if (!EqualityComparer<int>.Default.Equals(_restorableTweakCount, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.RestorableTweakCount);
				_restorableTweakCount = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.RestorableTweakCount);
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
	public IRelayCommand RefreshBackupsCommand => refreshBackupsCommand ?? (refreshBackupsCommand = new RelayCommand(RefreshBackups));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.2.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand OpenActivityLogCommand => openActivityLogCommand ?? (openActivityLogCommand = new RelayCommand(OpenActivityLog));

	public string BuildSupportSummary()
	{
		return "Quantix Elite support summary\nVersion: " + typeof(SettingsViewModel).Assembly.GetName().Version?.ToString() + "\nOS: " + RuntimeInformation.OSDescription + "\nArchitecture: " + RuntimeInformation.OSArchitecture.ToString() + "\nLogical processors: " + Environment.ProcessorCount + "\nUTC: " + DateTime.UtcNow.ToString("O") + "\n\nTracked tweak IDs (restore each from its feature page):\n" + string.Join("\n", _restore.GetRestorableTweakIds()) + "\n\nGaming-cleanup service backups are managed separately under Cleanup > Restore services.\nNo license key, HWID, account name, IP address or activity-log contents are included.\n";
	}

	private void ExportSupportSummary()
	{
		try
		{
			SaveFileDialog saveFileDialog = new SaveFileDialog
			{
				Title = "Save support summary",
				FileName = "Quantix-Elite-support.txt",
				Filter = "Text files (*.txt)|*.txt"
			};
			if (saveFileDialog.ShowDialog() == true)
			{
				File.WriteAllText(saveFileDialog.FileName, BuildSupportSummary());
				StatusMessage = "Support summary saved. You can review the text before sharing it.";
			}
		}
		catch (Exception ex)
		{
			StatusMessage = "Support summary could not be saved: " + ex.Message;
		}
	}

	public SettingsViewModel(IAppSettingsService settings, IRestoreService restore, ILoggingService logging)
	{
		_settings = settings;
		_restore = restore;
		_logging = logging;
		_launchAtSignIn = _settings.IsLaunchAtSignInEnabled();
		_confirmBeforeOptimizing = _settings.Settings.ConfirmBeforeOptimizing;
		_restorableTweakCount = _restore.GetRestorableTweakIds().Count;
		_isLoading = false;
		RefreshBackups();
	}

	[RelayCommand]
	private void RefreshBackups()
	{
		try
		{
			if (_restore is RestoreService { RecoveryWarning: not null } restoreService)
			{
				BackupSummary = restoreService.RecoveryWarning;
				OnPropertyChanged("BackupSummary");
				StatusMessage = restoreService.RecoveryWarning;
				return;
			}
			RestorableTweakCount = _restore.GetRestorableTweakIds().Count;
			BackupSummary = ((RestorableTweakCount == 0) ? "No tracked tweak backups." : string.Join(Environment.NewLine, _restore.GetRestorableTweakIds()));
			OnPropertyChanged("BackupSummary");
			StatusMessage = ((RestorableTweakCount == 0) ? "No Quantix changes are currently awaiting restore." : $"{RestorableTweakCount} Quantix change(s) can be restored from their feature pages.");
		}
		catch (Exception ex)
		{
			StatusMessage = "Backups could not be read: " + ex.Message;
		}
	}

	[RelayCommand]
	private void OpenActivityLog()
	{
		try
		{
			Process.Start(new ProcessStartInfo
			{
				FileName = _logging.CurrentLogFilePath,
				UseShellExecute = true
			});
			StatusMessage = "Opened today’s Quantix activity log.";
		}
		catch (Exception ex)
		{
			StatusMessage = "The activity log could not be opened: " + ex.Message;
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.2.0.0")]
	private void OnLaunchAtSignInChanged(bool value)
	{
		if (_isLoading || _revertingStartupSetting)
		{
			return;
		}
		try
		{
			_settings.SetLaunchAtSignIn(value);
			StatusMessage = (value ? "Quantix will launch when you sign in." : "Launch at sign-in is disabled.");
		}
		catch (Exception ex)
		{
			_revertingStartupSetting = true;
			LaunchAtSignIn = !value;
			_revertingStartupSetting = false;
			StatusMessage = ex.Message;
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.2.0.0")]
	private void OnConfirmBeforeOptimizingChanged(bool value)
	{
		if (_isLoading || _revertingConfirmation)
		{
			return;
		}
		bool confirmBeforeOptimizing = _settings.Settings.ConfirmBeforeOptimizing;
		try
		{
			_settings.Settings.ConfirmBeforeOptimizing = value;
			_settings.Save();
			StatusMessage = (value ? "Optimization confirmations are enabled." : "Optimization confirmations are disabled.");
		}
		catch (Exception ex)
		{
			_settings.Settings.ConfirmBeforeOptimizing = confirmBeforeOptimizing;
			_revertingConfirmation = true;
			try
			{
				ConfirmBeforeOptimizing = confirmBeforeOptimizing;
			}
			finally
			{
				_revertingConfirmation = false;
			}
			StatusMessage = "Preference was not saved: " + ex.Message;
		}
	}
}
