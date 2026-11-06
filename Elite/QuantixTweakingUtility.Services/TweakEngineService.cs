using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Win32;
using QuantixTweakingUtility.Models;

namespace QuantixTweakingUtility.Services;

public sealed class TweakEngineService : ITweakService
{
	private enum GpuVendor
	{
		Nvidia,
		Amd
	}

	private sealed record NetBiosAdapterState(string SettingId, uint Mode);

	private sealed record ServiceBackupState(int StartType, bool WasRunning, int? DelayedAutoStart = null, bool HasDelayBackup = false);

	private sealed record RegistryStringTarget(RegistryKey Hive, string AffectedSetting, string Path, string ValueName, string OptimizedValue);

	private sealed record RegistryDwordTarget(RegistryKey Hive, string AffectedSetting, string Path, string ValueName, int OptimizedValue);

	private const string HighPerformanceGuid = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";

	private const string QuantixElitePowerPlanName = "Quantix Elite Maximum Performance";

	private readonly ILoggingService _logging;

	private readonly IRestoreService _restore;

	private readonly List<TweakDefinition> _tweaks;

	private const string DisplayAdapterClassPath = "SYSTEM\\CurrentControlSet\\Control\\Class\\{4d36e968-e325-11ce-bfc1-08002be10318}";

	private const string NvidiaServicePath = "SYSTEM\\CurrentControlSet\\Services\\nvlddmkm";

	private const uint SpiGetKeyboardSpeed = 10u;

	private const uint SpiSetKeyboardSpeed = 11u;

	private const uint SpiSetKeyboardDelay = 23u;

	private const uint SpiGetKeyboardDelay = 22u;

	private const uint SpiGetMenuShowDelay = 106u;

	private const uint SpiSetMenuShowDelay = 107u;

	private const uint SpifUpdateIniFile = 1u;

	private const uint SpifSendChange = 2u;

	public TweakEngineService(ILoggingService logging, IRestoreService restore)
	{
		_logging = logging;
		_restore = restore;
		_tweaks = BuildCatalog().Select(tweak =>
		{
			var hasOriginal = tweak.HasSavedOriginal ?? (() => _restore.GetBackup(tweak.Id) != null);
			tweak.HasSavedOriginal = hasOriginal;
			return StabilityPolicy.Protect(tweak, hasOriginal);
		}).ToList();

		ValidateCatalog(_tweaks);
	}

	public IReadOnlyList<TweakDefinition> GetAllTweaks()
	{
		return _tweaks;
	}

	public IReadOnlyList<TweakDefinition> GetTweaksByCategory(TweakCategory category)
	{
		return _tweaks.Where((TweakDefinition t) => t.Category == category).ToList();
	}

	public async Task<TweakActionResult> ApplyTweakAsync(string tweakId)
	{
		TweakDefinition tweak = _tweaks.FirstOrDefault((TweakDefinition t) => t.Id == tweakId);
		if (tweak == null)
		{
			return TweakActionResult.Fail("Unknown tweak.");
		}
		if (tweak.RestoreOnly)
		{
			return TweakActionResult.Fail(tweak.Category == TweakCategory.Network ? "Use Optimize Network. Legacy controls are recovery only." : "This driver override has been retired for stability. Restore the saved original values instead, then restart Windows.");
		}
		if (tweak.RequiresAdministrator && !AdminHelper.IsRunningAsAdministrator())
		{
			string message = "\"" + tweak.Name + "\" requires administrator rights. Restart Quantix as administrator to apply it.";
			_logging.Warn("TweakEngine", message);
			return TweakActionResult.Fail(message);
		}
		try
		{
			TweakActionResult tweakActionResult = await tweak.ApplyAction();
			_logging.Info("TweakEngine", $"Apply '{tweak.Name}': {(tweakActionResult.Success ? "success" : "failed")} - {tweakActionResult.Message}");
			return tweakActionResult;
		}
		catch (Exception ex)
		{
			_logging.Error("TweakEngine", "Apply '" + tweak.Name + "' threw an exception.", ex);
			return TweakActionResult.Fail(ex is System.IO.IOException or System.ComponentModel.Win32Exception
				? ex.Message : "An unexpected error occurred. See the log for details.");
		}
	}

	public async Task<TweakActionResult> RestoreTweakAsync(string tweakId)
	{
		TweakDefinition tweak = _tweaks.FirstOrDefault((TweakDefinition t) => t.Id == tweakId);
		if (tweak == null)
		{
			return TweakActionResult.Fail("Unknown tweak.");
		}
		if (tweak.RequiresAdministrator && !AdminHelper.IsRunningAsAdministrator())
		{
			string message = "\"" + tweak.Name + "\" requires administrator rights. Restart Quantix as administrator to restore it.";
			_logging.Warn("TweakEngine", message);
			return TweakActionResult.Fail(message);
		}
		try
		{
			TweakActionResult tweakActionResult = await tweak.RestoreAction();
			_logging.Info("TweakEngine", $"Restore '{tweak.Name}': {(tweakActionResult.Success ? "success" : "failed")} - {tweakActionResult.Message}");
			return tweakActionResult;
		}
		catch (Exception ex)
		{
			_logging.Error("TweakEngine", "Restore '" + tweak.Name + "' threw an exception.", ex);
			return TweakActionResult.Fail(ex is System.IO.IOException or System.ComponentModel.Win32Exception
				? ex.Message : "An unexpected error occurred. See the log for details.");
		}
	}

	public async Task<List<TweakDefinition>> GetRecommendedTweaksAsync()
	{
		List<TweakDefinition> recommended = new List<TweakDefinition>();
		foreach (TweakDefinition tweak in _tweaks)
		{
			try
			{
				bool flag = !tweak.RestoreOnly && !tweak.IsOneTimeAction;
				if (flag)
				{
					flag = await tweak.CurrentStateReader() == TweakState.NotApplied;
				}
				if (flag && tweak.RiskLevel == TweakRiskLevel.Low)
				{
					recommended.Add(tweak);
				}
			}
			catch (Exception ex)
			{
				_logging.Error("TweakEngine", "Could not read state for '" + tweak.Name + "'.", ex);
			}
		}
		return recommended;
	}

	private List<TweakDefinition> BuildCatalog()
	{
		List<TweakDefinition> list = new List<TweakDefinition>
		{
			BuildPowerPlanTweak(),
			BuildPowerThrottlingTweak(),
			BuildHibernationTweak(),
			BuildDynamicTickTweak(),
			BuildHagsTweak()
		};
		if (HasDisplayAdapterVendor(GpuVendor.Nvidia))
		{
			list.AddRange(new TweakDefinition[6]
			{
				BuildNvidiaDriverLoggingTweak(),
				BuildNvidiaWriteCombiningTweak(),
				BuildNvidiaPreemptionTweak(),
				BuildNvidiaHiddenPowerSavingTweak(),
				BuildNvidiaP0StateTweak(),
				BuildNvidiaClockGatingTweak()
			});
			list.Add(new GpuMsiService().CreateTweak());
		}
		if (HasDisplayAdapterVendor(GpuVendor.Amd))
		{
			list.AddRange(new TweakDefinition[11]
			{
				BuildAmdPreemptionTweak(),
				BuildAmdDmaCopyTweak(),
				BuildAmdBlockWriteTweak(),
				BuildAmdStutterModeTweak(),
				BuildAmdUlpsTweak(),
				BuildAmdSclkDeepSleepTweak(),
				BuildAmdLightSleepTweak(),
				BuildAmdGpuPowerDownTweak(),
				BuildAmdGpioPowerSavingTweak(),
				BuildAmdPowerGatingTweak(),
				BuildAmdClockGatingTweak()
			});
		}
		list.AddRange(new TweakDefinition[]
		{
			BuildMemoryCompressionTweak(),
			BuildPageCombiningTweak(),
			BuildProcessSchedulingTweak(),
			BuildUsbSelectiveSuspendTweak(),
			PrecisionTweaks.BuildAcceleration(_restore),
			BuildKeyboardResponseTweak(),
			BuildTrimTweak(),
			BuildSysMainServiceTweak(),
			BuildSearchIndexingServiceTweak(),
			BuildVisualEffectsTweak(),
			BuildBackgroundAppsTweak(),
			BuildShowFileExtensionsTweak(),
			BuildSearchWebSuggestionsTweak(),
			BuildWindowsTipsTweak(),
			BuildFastStartupTweak(),
			BuildRotatingLockScreenTweak(),
			BuildTailoredExperiencesTweak(),
			BuildFeedbackNotificationsTweak(),
			BuildAdvertisingIdTweak(),
			BuildAppLaunchTrackingTweak(),
			BuildMenuDelayTweak(),
			BuildAeroShakeTweak(),
			BuildFaultTolerantHeapTweak(),
			BuildInsiderBuildsTweak(),
			BuildFastShutdownTweak(),
			BuildResourcePolicyTweak(),
			BuildSingleExplorerProcessTweak(),
			BuildAudioDuckingTweak(),
			BuildWidgetsAndNewsTweak(),
			BuildClipboardHistoryTweak(),
			BuildStorageSenseTweak(),
			BuildSettingsBackupTweak(),
			BuildDefenderScanCpuTweak(),
			BuildRecallSnapshotsTweak(),
			BuildEdgeBackgroundTweak(),
			BuildChromeBackgroundTweak(),
			BuildChromeReportingTweak(),
			BuildFirefoxTelemetryTweak(),
			BuildLongPathsTweak(),
			BuildCrashAutoRestartTweak(),
			BuildOneDriveFileSyncTweak(),
			BuildStoreAutoUpdatesTweak(),
			BuildDriverUpdatesTweak(),
			BuildCeipTweak(),
			BuildActivityHistoryTweak(),
			BuildWindowsErrorReportingTweak(),
			BuildLanguageHeaderTweak(),
			BuildDynamicSearchBoxTweak(),
			BuildConsumerFeaturesTweak(),
			BuildInputPersonalizationTweak(),
			BuildLocationAccessTweak(),
			BuildDiagnosticInfoAccessTweak(),
			BuildNotificationAccessTweak(),
			BuildTelemetryTweak(),
			BuildTypingInsightsTweak(),
			BuildSpeechDataCollectionTweak(),
			BuildCloudContentOptimizationTweak(),
			BuildOnlineTipsTweak(),
			BuildAutomaticWindowsUpdatesTweak(),
			BuildFastUserSwitchingTweak(),
			BuildAutomaticMaintenanceTweak(),
			BuildSleepStudyTweak(),
			BuildAccessibilityPromptsTweak(),
			BuildDarkModeTweak(),
			BuildNotificationCenterTweak(),
			BuildClassicAltTabTweak(),
			BuildTransparencyTweak(),
			BuildHideNetworkInExplorerTweak(),
			BuildHideGalleryInExplorerTweak(),
			BuildHideHomeInExplorerTweak(),
			BuildClassicContextMenuTweak(),
			BuildPreviousVersionsMenuTweak(),
			BuildCpuSideChannelMitigationsTweak(),
			BuildHvciTweak(),
			BuildVbsTweak(),
			BuildHypervisorLaunchTweak(),
			BuildSmartScreenTweak(),
			BuildMarkOfTheWebTweak(),
			BuildSystemRestoreServicesTweak(),
			BuildVpnServicesTweak(),
			BuildWifiServicesTweak(),
			BuildBluetoothServicesTweak(),
			BuildPrintingServicesTweak(),
			BuildDiagnosticsServicesTweak(),
			StartupFirewallTweak.Build(),
			TaskSchedulerFirewallTweak.Build(_restore),
			BuildServiceGroupTweak("processes.compatibility.disable", "Disable Compatibility Services",
				"Disables Program Compatibility Assistant (PcaSvc) and Windows Font Cache (FontCache). Compatibility troubleshooting and font caching become unavailable; some older applications or font-heavy apps may be affected.",
				TweakRiskLevel.Medium, new[] { "PcaSvc", "FontCache" }),
			BuildServiceGroupTweak("processes.delivery-service.disable", "Disable Update Delivery Service",
				"Disables the Delivery Optimization service (DoSvc). Windows and Store download delivery features may be affected. This is separate from switching off peer-to-peer sharing in Network Optimization.",
				TweakRiskLevel.High, new[] { "DoSvc" }),
			BuildServiceGroupTweak("processes.biometric.disable", "Disable Biometric Service",
				"Disables Windows Biometric Service (WbioSrvc). Fingerprint and facial-recognition sign-in stop working. Keep another working sign-in method and restore this group before using biometric hardware again.",
				TweakRiskLevel.High, new[] { "WbioSrvc" }),
			BuildServiceGroupTweak("processes.vendor-services.disable", "Vendor Services Off",
				"Stops and disables the supported Razer Game Manager and Elevation, Samsung connection, Canon extended-survey and AMD Crash Defender services when installed. Razer profiles, Samsung DeX/phone connections, Canon usage reporting and AMD crash recovery may stop working. Saves original startup, delayed-start and running states for restore. Missing services are skipped; unrelated vendor services and kernel drivers remain unchanged.",
				TweakRiskLevel.High, new[] { "Razer Game Manager Service", "Razer Game Manager Service 3", "GameManagerService3", "razer_elevation_service", "ss_conn_service2", "IJPLMSVC", "AMD Crash Defender Service" }),
			BuildServiceGroupTweak("services.update-store.disable", "Disable Windows Update / Store Services",
				"Disables Windows Update (wuauserv), Background Intelligent Transfer Service (BITS), and Microsoft Store Install Service (InstallService). Windows and Store updates and other BITS downloads may stop. Restore before updating. Windows servicing may re-enable protected services.",
				TweakRiskLevel.High, new[] { "wuauserv", "BITS", "InstallService" }),
			BuildServiceGroupTweak("services.remote-desktop.disable", "Disable Remote Desktop Services",
				"Disables Remote Desktop Services, Remote Desktop Configuration, and its user-mode port redirector. Inbound Remote Desktop sessions and related redirection stop working. Use only on a PC that does not need remote access.",
				TweakRiskLevel.High, new[] { "TermService", "SessionEnv", "UmRdpService" }),
			BuildServiceGroupTweak("services.discovery.disable", "Disable Discovery Services",
				"Disables SSDP Discovery, UPnP Device Host, Function Discovery Provider Host and Function Discovery Resource Publication. Network device discovery and publishing this PC on the network may stop. Direct connections by address are separate.",
				TweakRiskLevel.Medium, new[] { "SSDPSRV", "upnphost", "fdPHost", "FDResPub" }),
			BuildServiceGroupTweak("services.legacy-features.disable", "Disable Legacy Feature Services",
				"Disables Fax, Remote Registry, Retail Demo, Downloaded Maps Manager, Phone Service and Distributed Link Tracking Client when present. These features and applications that depend on them become unavailable. Missing services are skipped; no Windows components are uninstalled.",
				TweakRiskLevel.High, new[] { "Fax", "RemoteRegistry", "RetailDemo", "MapsBroker", "PhoneSvc", "TrkWks" }),
			BuildGameModeTweak(),
			BuildGameBarCaptureTweak(),
			BuildMmcssPriorityTweak(),
			BuildNetworkThrottlingTweak(),
			BuildDeliveryOptimizationTweak(),
			BuildSmartDnsResolutionTweak(),
			BuildNagleAlgorithmTweak(),
			BuildTcpHeuristicsTweak(),
			BuildNetBiosTweak()
		});
		list.AddRange(PrecisionTweaks.Build(_restore));
		list.AddRange(NvidiaCardTweaks.Build());
		list.AddRange(PowerPolicyTweaks.Build(_restore));
		list.Add(new PcieLinkPowerService().CreateTweak());
		list.Add(AdapterRssTweak.Build(_restore));
		list.Add(RssPlacementTweak.Build(_restore));
		list.Add(PrefetchTuningTweak.Build(_restore));
		list.Add(KernelMemoryTweak.Build(_restore));
		list.Add(GameIndexingTweak.Build(_restore));
		list.Add(AdapterOffloadTweaks.Build(_restore, true));
		list.Add(AdapterOffloadTweaks.Build(_restore, false));
		list.AddRange(AdapterAdvancedTweaks.Build(_restore));
		list.AddRange(WifiDriverTweaks.Build(_restore));
		list.Add(WifiPowerTweak.Build(_restore));
		list.Add(TcpTimeWaitTweak.Build(_restore));
		list.Add(NetworkResetTweak.Build(new WindowsNetworkResetRunner(_logging)));
		list.Add(DriveOptimizationTweak.Build(new WindowsDriveOptimizer(_logging)));
		list.Add(AdapterDnsTweak.Build(_restore));
		list.Add(UpdatePauseTweak.Build(_restore));
		list.Add(TimerResolutionTweak.Build());
		list.Add(MmcssSchedulingTweaks.AudioWindow(_restore));
		list.Add(ProtectionControlTweaks.Uac(_restore));
		list.Add(ProtectionControlTweaks.Tdr(_restore));
		list.Add(InputQueueTweaks.Build(false,_restore));
		list.Add(InputQueueTweaks.Build(true,_restore));
		list.Add(BrowserUpdaterTweak.Build(_restore));
		list.Add(PciMsiTweaks.Build(PciMsiKind.Network,_restore));
		list.Add(PciMsiTweaks.Build(PciMsiKind.Usb,_restore));
		list.Add(PciMsiTweaks.Build(PciMsiKind.Nvme,_restore));
		list.Add(BootOptionTweaks.Build(false,_restore));
		list.Add(BootOptionTweaks.Build(true,_restore));
		list.Add(DiscordAccelerationTweak.Build(_restore));
		list.Add(WindowsTelemetryTasksTweak.Build(_restore));
		var cleanup = new CleanupService(_logging);
		list.Add(WindowsCleanupActions.Cache(false, cleanup));
		list.Add(WindowsDiskCleanupTweak.Build());
		list.Add(WindowsCleanupActions.Cache(true, cleanup));
		list.Add(WindowsCleanupActions.RecycleBin());
		list.Add(NeverSleepAcTweak.Build(_restore));
		list.Add(GlobalNetworkTweaks.Build(GlobalNetworkSetting.TaskOffload, _restore));
		list.Add(GlobalNetworkTweaks.Build(GlobalNetworkSetting.Teredo, _restore));
		list.Add(GlobalNetworkTweaks.Build(GlobalNetworkSetting.Timestamps, _restore));
		list.Add(GlobalNetworkTweaks.Build(GlobalNetworkSetting.Ecn, _restore));
		list.Add(WindowsRepairActions.Build(WindowsRepairOperation.SystemFileChecker));
		list.Add(WindowsRepairActions.Build(WindowsRepairOperation.RestoreHealth));
		list.Add(InstantProcessCleanup.Build(true));
		list.Add(AutoLoggerTelemetryTweak.Build(_restore));
		list.Add(InstantProcessCleanup.Build(false));
		var network = new NetworkInfoService(_logging);
		list.Add(new TweakDefinition
		{
			Id = "network.flush-dns-renew", Name = "Flush DNS + Renew",
			Description = "Flushes the Windows DNS resolver cache and renews the active physical adapter's DHCP lease. May briefly interrupt connections. This is a one-time action, with no persistent setting to restore.",
			Category = TweakCategory.Network, RiskLevel = TweakRiskLevel.High, RequiresAdministrator = true, IsOneTimeAction = true,
			AffectedSettings = new[] { "action:DNS:flush-DHCP:renew" },
			CurrentStateReader = () => Task.FromResult(TweakState.NotApplied),
			ApplyAction = async () => { var result = await network.FlushDnsAndRenewAsync(); return result.Success ? TweakActionResult.Ok(result.Message) : TweakActionResult.Fail(result.Message); },
			RestoreAction = () => Task.FromResult(TweakActionResult.Fail("DNS flush and DHCP renewal are one-time actions with no persistent setting to restore."))
		});
		return list;
	}

	private static void ValidateCatalog(IReadOnlyCollection<TweakDefinition> tweaks)
	{
		IGrouping<string, TweakDefinition> grouping = tweaks.GroupBy<TweakDefinition, string>((TweakDefinition tweak) => tweak.Id, StringComparer.OrdinalIgnoreCase).FirstOrDefault((IGrouping<string, TweakDefinition> group) => group.Count() > 1);
		if (grouping != null)
		{
			throw new InvalidOperationException("Duplicate tweak ID registered: " + grouping.Key);
		}
		IGrouping<string, (string, TweakDefinition)> grouping2 = tweaks.SelectMany((TweakDefinition tweak) => tweak.AffectedSettings.Select((string setting) => (Setting: setting, Tweak: tweak))).GroupBy<(string, TweakDefinition), string>(((string Setting, TweakDefinition Tweak) item) => item.Setting, StringComparer.OrdinalIgnoreCase).FirstOrDefault((IGrouping<string, (string Setting, TweakDefinition Tweak)> group) => {
			var ids = group.Select(item => item.Tweak.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
			return ids.Count > 1;
		});
		if (grouping2 != null)
		{
			string text = string.Join(", ", grouping2.Select<(string, TweakDefinition), string>(((string Setting, TweakDefinition Tweak) item) => item.Tweak.Id).Distinct());
			throw new InvalidOperationException("Windows setting '" + grouping2.Key + "' is owned by multiple tweaks: " + text);
		}
	}

	private TweakDefinition BuildPowerPlanTweak()
	{
		TweakDefinition tweakDefinition = new TweakDefinition();
		tweakDefinition.Id = "cpu.power-plan.quantix-elite";
		tweakDefinition.Name = "Quantix Elite Maximum Performance";
		tweakDefinition.Description = "Creates a Quantix custom gaming plan: CPU performance range 100%, PCIe link power saving off, display timeout Never (including the lock screen), and automatic sleep/hibernate Never on AC and battery. Uses more power and may increase heat and battery drain. Restoring returns to your previously active plan. FPS gains depend on hardware and game.";
		tweakDefinition.Category = TweakCategory.Cpu;
		tweakDefinition.RiskLevel = TweakRiskLevel.Low;
		tweakDefinition.RequiresAdministrator = false;
		tweakDefinition.RequiresRestart = false;
		tweakDefinition.AffectedSettings = new string[1] { "powercfg:scheme:quantix-elite" };
		tweakDefinition.CurrentStateReader = async delegate
		{
			string text = await RunPowercfgAsync("/getactivescheme");
			return (text != null) ? (text.Contains("Quantix Elite Maximum Performance", StringComparison.OrdinalIgnoreCase) ? TweakState.Applied : TweakState.NotApplied) : TweakState.Unknown;
		};
		tweakDefinition.ApplyAction = async delegate
		{
			string text = await RunPowercfgAsync("/getactivescheme");
			if (text == null || !text.Contains("Quantix Elite Maximum Performance", StringComparison.OrdinalIgnoreCase))
			{
				string text2 = ExtractSchemeGuid(text);
				if (text2 != null && _restore.GetBackup("cpu.power-plan.quantix-elite") == null)
				{
					_restore.SaveBackup("cpu.power-plan.quantix-elite", text2);
				}
			}
			string targetGuid = ExtractSchemeGuidByName(await RunPowercfgAsync("/list"), "Quantix Elite Maximum Performance");
			if (targetGuid == null)
			{
				targetGuid = ExtractSchemeGuid(await RunPowercfgAsync("/duplicatescheme 381b4222-f694-41f0-9685-ff5bb260df2e"));
				if (targetGuid == null)
				{
					return TweakActionResult.Fail("Could not create the Quantix Elite power plan.");
				}
				await RunPowercfgAsync($"/changename {targetGuid} \"{"Quantix Elite Maximum Performance"}\" " + "\"Custom maximum-performance plan created by Quantix Tweaking Utility Elite.\"");
			}
			return (!(await QuantixPowerSettings.ConfigureAsync(targetGuid, async (string command) => await RunPowercfgAsync(command) != null))) ? TweakActionResult.Fail("Windows rejected a custom power setting. The plan was not activated; restore your previous plan if needed.") : ((await RunPowercfgAsync("/setactive " + targetGuid) != null) ? TweakActionResult.Ok("Quantix Elite custom gaming plan is active. Display, automatic sleep and hibernate timeouts are Never on AC and battery.") : TweakActionResult.Fail("powercfg did not respond as expected."));
		};
		tweakDefinition.RestoreAction = async delegate
		{
			string backup = _restore.GetBackup("cpu.power-plan.quantix-elite");
			if (backup == null)
			{
				return TweakActionResult.Fail("No previous power plan was recorded.");
			}
			string obj = await RunPowercfgAsync("/setactive " + backup);
			if (obj != null)
			{
				_restore.ClearBackup("cpu.power-plan.quantix-elite");
			}
			return (obj != null) ? TweakActionResult.Ok("Restored your previous power plan.") : TweakActionResult.Fail("powercfg did not respond as expected.");
		};
		return tweakDefinition;
	}

	private TweakDefinition BuildUsbSelectiveSuspendTweak() => UsbSelectiveSuspendTweak.Build(_restore);

	private TweakDefinition BuildVisualEffectsTweak()
	{
		TweakDefinition tweakDefinition = new TweakDefinition();
		tweakDefinition.Id = "perf.visual-effects.best-performance";
		tweakDefinition.Name = "Visual Effects: Best Performance";
		tweakDefinition.Description = "Sets the Windows visual effects preference Explorer itself reads (the same setting as System Properties > Advanced > Performance). Reduces animation and transparency overhead. Sign out and back in for it to fully take effect.";
		tweakDefinition.Category = TweakCategory.Windows;
		tweakDefinition.RiskLevel = TweakRiskLevel.Low;
		tweakDefinition.RequiresAdministrator = false;
		tweakDefinition.RequiresRestart = true;
		tweakDefinition.AffectedSettings = new string[1] { "HKCU\\Explorer\\VisualEffects:VisualFXSetting" };
		tweakDefinition.CurrentStateReader = () => Task.FromResult((RegistryHelper.ReadDword(Registry.CurrentUser, "Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\VisualEffects", "VisualFXSetting") == 2) ? TweakState.Applied : TweakState.NotApplied);
		tweakDefinition.ApplyAction = delegate
		{
			int valueOrDefault = RegistryHelper.ReadDword(Registry.CurrentUser, "Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\VisualEffects", "VisualFXSetting").GetValueOrDefault();
			_restore.SaveBackup("perf.visual-effects.best-performance", valueOrDefault.ToString());
			RegistryHelper.WriteDword(Registry.CurrentUser, "Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\VisualEffects", "VisualFXSetting", 2);
			return Task.FromResult(TweakActionResult.Ok("Visual effects set to best performance. Sign out and back in for full effect."));
		};
		tweakDefinition.RestoreAction = delegate
		{
			int result;
			int value = (int.TryParse(_restore.GetBackup("perf.visual-effects.best-performance"), out result) ? result : 0);
			RegistryHelper.WriteDword(Registry.CurrentUser, "Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\VisualEffects", "VisualFXSetting", value);
			_restore.ClearBackup("perf.visual-effects.best-performance");
			return Task.FromResult(TweakActionResult.Ok("Visual effects restored."));
		};
		return tweakDefinition;
	}

	private TweakDefinition BuildBackgroundAppsTweak()
	{
		TweakDefinition tweakDefinition = new TweakDefinition();
		tweakDefinition.Id = "perf.background-apps.disable";
		tweakDefinition.Name = "Disable Background Apps";
		tweakDefinition.Description = "Stops Store/UWP apps from running and refreshing content while not in focus. Reduces idle CPU and network use. Behavior of this specific setting varies slightly by Windows build.";
		tweakDefinition.Category = TweakCategory.Windows;
		tweakDefinition.RiskLevel = TweakRiskLevel.Low;
		tweakDefinition.RequiresAdministrator = false;
		tweakDefinition.RequiresRestart = false;
		tweakDefinition.AffectedSettings = new string[1] { "HKCU\\BackgroundAccessApplications:GlobalUserDisabled" };
		tweakDefinition.CurrentStateReader = () => Task.FromResult((RegistryHelper.ReadDword(Registry.CurrentUser, "Software\\Microsoft\\Windows\\CurrentVersion\\BackgroundAccessApplications", "GlobalUserDisabled") == 1) ? TweakState.Applied : TweakState.NotApplied);
		tweakDefinition.ApplyAction = delegate
		{
			int valueOrDefault = RegistryHelper.ReadDword(Registry.CurrentUser, "Software\\Microsoft\\Windows\\CurrentVersion\\BackgroundAccessApplications", "GlobalUserDisabled").GetValueOrDefault();
			_restore.SaveBackup("perf.background-apps.disable", valueOrDefault.ToString());
			RegistryHelper.WriteDword(Registry.CurrentUser, "Software\\Microsoft\\Windows\\CurrentVersion\\BackgroundAccessApplications", "GlobalUserDisabled", 1);
			return Task.FromResult(TweakActionResult.Ok("Background apps disabled."));
		};
		tweakDefinition.RestoreAction = delegate
		{
			int result;
			int value = (int.TryParse(_restore.GetBackup("perf.background-apps.disable"), out result) ? result : 0);
			RegistryHelper.WriteDword(Registry.CurrentUser, "Software\\Microsoft\\Windows\\CurrentVersion\\BackgroundAccessApplications", "GlobalUserDisabled", value);
			_restore.ClearBackup("perf.background-apps.disable");
			return Task.FromResult(TweakActionResult.Ok("Background apps setting restored."));
		};
		return tweakDefinition;
	}

	private TweakDefinition BuildGameModeTweak()
	{
		// Retain the previous backup key so earlier saved originals remain usable.
		const string backupId = "gaming.game-mode.disable";
		const string path = @"Software\Microsoft\GameBar";
		const string name = "AutoGameModeEnabled";
		const int target = 1;
		return new TweakDefinition
		{
			Id = "gaming.game-mode.enable",
			Name = "Game Mode On",
			HasSavedOriginal = () => _restore.GetBackup(backupId) != null,
			Description = "Enables Windows Game Mode for supported games. Restoring returns to the original setting saved by Elite.",
			Category = TweakCategory.Gaming,
			RiskLevel = TweakRiskLevel.Medium,
			AffectedSettings = new[] { @"HKCU\GameBar:AutoGameModeEnabled" },
			CurrentStateReader = () => Task.FromResult(
				RegistryHelper.ReadDword(Registry.CurrentUser, path, name) == target ? TweakState.Applied : TweakState.NotApplied),
			ApplyAction = () =>
			{
				int? prior = RegistryHelper.ReadDword(Registry.CurrentUser, path, name);
				if (prior == target)
					return Task.FromResult(TweakActionResult.Ok("Game Mode already matches this setting."));
				if (_restore.GetBackup(backupId) == null)
					_restore.SaveBackup(backupId, prior?.ToString() ?? "absent");
				RegistryHelper.WriteDword(Registry.CurrentUser, path, name, target);
				return Task.FromResult(TweakActionResult.Ok("Game Mode enabled."));
			},
			RestoreAction = () =>
			{
				string? backup = _restore.GetBackup(backupId);
				if (backup == "absent")
					RegistryHelper.DeleteValue(Registry.CurrentUser, path, name);
				else if (int.TryParse(backup, out int prior))
					RegistryHelper.WriteDword(Registry.CurrentUser, path, name, prior);
				else
					return Task.FromResult(TweakActionResult.Fail("No saved Game Mode setting is available."));
				_restore.ClearBackup(backupId);
				return Task.FromResult(TweakActionResult.Ok("Game Mode setting restored."));
			}
		};
	}

	private TweakDefinition BuildPowerThrottlingTweak()
	{
		TweakDefinition tweakDefinition = new TweakDefinition();
		tweakDefinition.Id = "perf.power-throttling.disable";
		tweakDefinition.Name = "Disable Power Throttling";
		tweakDefinition.Description = "Stops Windows from deliberately capping CPU performance for background/unfocused processes (EcoQoS). Can noticeably increase power draw and heat, especially on laptops - recommended mainly for desktops.";
		tweakDefinition.Category = TweakCategory.Cpu;
		tweakDefinition.RiskLevel = TweakRiskLevel.Medium;
		tweakDefinition.RequiresAdministrator = true;
		tweakDefinition.RequiresRestart = false;
		tweakDefinition.AffectedSettings = new string[1] { "HKLM\\Power\\PowerThrottling:PowerThrottlingOff" };
		tweakDefinition.CurrentStateReader = () => Task.FromResult((RegistryHelper.ReadDword(Registry.LocalMachine, "SYSTEM\\CurrentControlSet\\Control\\Power\\PowerThrottling", "PowerThrottlingOff") == 1) ? TweakState.Applied : TweakState.NotApplied);
		tweakDefinition.ApplyAction = delegate
		{
			int? num = RegistryHelper.ReadDword(Registry.LocalMachine, "SYSTEM\\CurrentControlSet\\Control\\Power\\PowerThrottling", "PowerThrottlingOff");
			_restore.SaveBackup("perf.power-throttling.disable", num?.ToString() ?? "null");
			RegistryHelper.WriteDword(Registry.LocalMachine, "SYSTEM\\CurrentControlSet\\Control\\Power\\PowerThrottling", "PowerThrottlingOff", 1);
			return Task.FromResult(TweakActionResult.Ok("Power throttling disabled system-wide."));
		};
		tweakDefinition.RestoreAction = delegate
		{
			string backup = _restore.GetBackup("perf.power-throttling.disable");
			int result;
			if (backup == null || backup == "null")
			{
				RegistryHelper.DeleteValue(Registry.LocalMachine, "SYSTEM\\CurrentControlSet\\Control\\Power\\PowerThrottling", "PowerThrottlingOff");
			}
			else if (int.TryParse(backup, out result))
			{
				RegistryHelper.WriteDword(Registry.LocalMachine, "SYSTEM\\CurrentControlSet\\Control\\Power\\PowerThrottling", "PowerThrottlingOff", result);
			}
			_restore.ClearBackup("perf.power-throttling.disable");
			return Task.FromResult(TweakActionResult.Ok("Power throttling setting restored."));
		};
		return tweakDefinition;
	}

	private TweakDefinition BuildHibernationTweak()
	{
		TweakDefinition tweakDefinition = new TweakDefinition();
		tweakDefinition.Id = "power.hibernation.disable";
		tweakDefinition.Name = "Disable Hibernation";
		tweakDefinition.Description = "Turns off Windows hibernation and removes hiberfil.sys, reclaiming disk space. Fast Startup depends on hibernation and will also be unavailable until this setting is restored.";
		tweakDefinition.Category = TweakCategory.Cpu;
		tweakDefinition.RiskLevel = TweakRiskLevel.Medium;
		tweakDefinition.RequiresAdministrator = true;
		tweakDefinition.RequiresRestart = false;
		tweakDefinition.AffectedSettings = new string[1] { "powercfg:hibernate" };
		tweakDefinition.HasSavedOriginal = () => _restore.GetBackup("power.hibernation.disable") != null;
		tweakDefinition.CurrentStateReader = delegate
		{
			bool? hibernationEnabled = GetHibernationEnabled();
			return Task.FromResult((!hibernationEnabled.HasValue) ? TweakState.Unsupported : ((!hibernationEnabled.Value) ? TweakState.Applied : TweakState.NotApplied));
		};
		tweakDefinition.ApplyAction = async delegate
		{
			bool? hibernationEnabled = GetHibernationEnabled();
			if (!hibernationEnabled.HasValue)
			{
				return TweakActionResult.Fail("Windows hibernation state is unavailable on this system.");
			}
			string? saved = _restore.GetBackup("power.hibernation.disable");
			if (saved != null && !bool.TryParse(saved, out _)) return TweakActionResult.Fail("The saved hibernation state is invalid; retained for review.");
			if (saved == null)
			{
				saved = hibernationEnabled.Value.ToString();
				_restore.SaveBackup("power.hibernation.disable", saved);
				if (_restore.GetBackup("power.hibernation.disable") != saved) return TweakActionResult.Fail("The original hibernation state could not be saved. Nothing changed.");
			}
			if (hibernationEnabled.Value && await RunPowercfgAsync("/hibernate off") == null) return TweakActionResult.Fail("Windows could not disable hibernation; original retained.");
			return GetHibernationEnabled() == false ? TweakActionResult.Ok("Hibernation disabled and verified through Windows power capabilities.") : TweakActionResult.Fail("Hibernation verification failed; original retained.");
		};
		tweakDefinition.RestoreAction = async delegate
		{
			if (!bool.TryParse(_restore.GetBackup("power.hibernation.disable"), out var result))
			{
				return TweakActionResult.Fail("No previous hibernation state was recorded.");
			}
			string obj = await RunPowercfgAsync(result ? "/hibernate on" : "/hibernate off");
			bool verified = obj != null && GetHibernationEnabled() == result;
			if (verified)
			{
				_restore.ClearBackup("power.hibernation.disable");
			}
			return verified ? TweakActionResult.Ok("Hibernation enabled state restored and verified.") : TweakActionResult.Fail("Windows could not verify restored hibernation; original retained.");
		};
		return tweakDefinition;
	}

	private TweakDefinition BuildDynamicTickTweak() => BootOptionTweaks.Dynamic(_restore);

	private TweakDefinition BuildHagsTweak()
	{
		TweakDefinition tweakDefinition = new TweakDefinition();
		tweakDefinition.Id = "gaming.hags.enable";
		tweakDefinition.Name = "Hardware-Accelerated GPU Scheduling";
		tweakDefinition.Description = "Lets the GPU manage its own video memory scheduling instead of relying on the Windows kernel scheduler, which can reduce latency on supported GPUs and drivers. Requires a restart to take effect. On some older GPUs or drivers this can cause display instability - if you notice issues after enabling and restarting, restore this setting.";
		tweakDefinition.Category = TweakCategory.Gpu;
		tweakDefinition.RiskLevel = TweakRiskLevel.Medium;
		tweakDefinition.RequiresAdministrator = true;
		tweakDefinition.RequiresRestart = true;
		tweakDefinition.AffectedSettings = new string[1] { "HKLM\\GraphicsDrivers:HwSchMode" };
		tweakDefinition.CurrentStateReader = () => Task.FromResult((RegistryHelper.ReadDword(Registry.LocalMachine, "SYSTEM\\CurrentControlSet\\Control\\GraphicsDrivers", "HwSchMode") == 2) ? TweakState.Applied : TweakState.NotApplied);
		tweakDefinition.ApplyAction = delegate
		{
			int? num = RegistryHelper.ReadDword(Registry.LocalMachine, "SYSTEM\\CurrentControlSet\\Control\\GraphicsDrivers", "HwSchMode");
			_restore.SaveBackup("gaming.hags.enable", num?.ToString() ?? "null");
			RegistryHelper.WriteDword(Registry.LocalMachine, "SYSTEM\\CurrentControlSet\\Control\\GraphicsDrivers", "HwSchMode", 2);
			return Task.FromResult(TweakActionResult.Ok("GPU scheduling enabled. Restart your PC for it to take effect."));
		};
		tweakDefinition.RestoreAction = delegate
		{
			string backup = _restore.GetBackup("gaming.hags.enable");
			int result;
			if (backup == null || backup == "null")
			{
				RegistryHelper.DeleteValue(Registry.LocalMachine, "SYSTEM\\CurrentControlSet\\Control\\GraphicsDrivers", "HwSchMode");
			}
			else if (int.TryParse(backup, out result))
			{
				RegistryHelper.WriteDword(Registry.LocalMachine, "SYSTEM\\CurrentControlSet\\Control\\GraphicsDrivers", "HwSchMode", result);
			}
			_restore.ClearBackup("gaming.hags.enable");
			return Task.FromResult(TweakActionResult.Ok("GPU scheduling setting restored. Restart your PC for it to take effect."));
		};
		return tweakDefinition;
	}

	private static bool HasDisplayAdapterVendor(GpuVendor vendor)
	{
		return GetDisplayAdapterRegistryPaths(vendor).Count > 0;
	}

	private static IReadOnlyList<string> GetDisplayAdapterRegistryPaths(GpuVendor vendor)
	{
		List<string> list = new List<string>();
		try
		{
			using RegistryKey registryKey = Registry.LocalMachine.OpenSubKey("SYSTEM\\CurrentControlSet\\Control\\Class\\{4d36e968-e325-11ce-bfc1-08002be10318}", writable: false);
			if (registryKey == null)
			{
				return list;
			}
			foreach (string item in (from name in registryKey.GetSubKeyNames()
				where Regex.IsMatch(name, "^\\d{4}$")
				select name).OrderBy<string, string>((string name) => name, StringComparer.OrdinalIgnoreCase))
			{
				using RegistryKey registryKey2 = registryKey.OpenSubKey(item, writable: false);
				if (registryKey2 != null)
				{
					string text = string.Join('|', registryKey2.GetValue("ProviderName")?.ToString(), registryKey2.GetValue("DriverDesc")?.ToString(), registryKey2.GetValue("MatchingDeviceId")?.ToString());
					if (vendor switch
					{
						GpuVendor.Nvidia => text.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) || text.Contains("VEN_10DE", StringComparison.OrdinalIgnoreCase), 
						GpuVendor.Amd => text.Contains("AMD", StringComparison.OrdinalIgnoreCase) || text.Contains("RADEON", StringComparison.OrdinalIgnoreCase) || text.Contains("VEN_1002", StringComparison.OrdinalIgnoreCase), 
						_ => false, 
					})
					{
						list.Add("SYSTEM\\CurrentControlSet\\Control\\Class\\{4d36e968-e325-11ce-bfc1-08002be10318}\\" + item);
					}
				}
			}
		}
		catch
		{
		}
		return list;
	}

	private static IReadOnlyList<RegistryDwordTarget> BuildGpuClassDwordTargets(GpuVendor vendor, params (string ValueName, int OptimizedValue)[] values)
	{
		return GetDisplayAdapterRegistryPaths(vendor).SelectMany((string path) => values.Select(((string ValueName, int OptimizedValue) value) => new RegistryDwordTarget(Registry.LocalMachine, "HKLM\\" + path + ":" + value.ValueName, path, value.ValueName, value.OptimizedValue))).ToArray();
	}

	private TweakDefinition BuildNvidiaDriverLoggingTweak()
	{
		return BuildRegistryDwordSetTweak("gpu.nvidia.driver-logging.disable", "Disable NVIDIA Driver Logging", "Disables NVIDIA driver error, event, paging, and warning log writes. This can remove minor background logging overhead, but it also reduces driver diagnostics. Re-enable it before troubleshooting display-driver problems.", TweakCategory.Gpu, TweakRiskLevel.Medium, new RegistryDwordTarget[4]
		{
			new RegistryDwordTarget(Registry.LocalMachine, "HKLM\\nvlddmkm:LogErrorEntries", "SYSTEM\\CurrentControlSet\\Services\\nvlddmkm", "LogErrorEntries", 0),
			new RegistryDwordTarget(Registry.LocalMachine, "HKLM\\nvlddmkm:LogEventEntries", "SYSTEM\\CurrentControlSet\\Services\\nvlddmkm", "LogEventEntries", 0),
			new RegistryDwordTarget(Registry.LocalMachine, "HKLM\\nvlddmkm:LogPagingEntries", "SYSTEM\\CurrentControlSet\\Services\\nvlddmkm", "LogPagingEntries", 0),
			new RegistryDwordTarget(Registry.LocalMachine, "HKLM\\nvlddmkm:LogWarningEntries", "SYSTEM\\CurrentControlSet\\Services\\nvlddmkm", "LogWarningEntries", 0)
		}, requiresAdministrator: true, requiresRestart: true);
	}

	private TweakDefinition BuildNvidiaWriteCombiningTweak()
	{
		List<RegistryDwordTarget> list = new List<RegistryDwordTarget>
		{
			new RegistryDwordTarget(Registry.LocalMachine, "HKLM\\nvlddmkm:DisableWriteCombining", "SYSTEM\\CurrentControlSet\\Services\\nvlddmkm", "DisableWriteCombining", 1)
		};
		list.AddRange(BuildGpuClassDwordTargets(GpuVendor.Nvidia, ("RmForceDisableIomapWC", 1)));
		return BuildRegistryDwordSetTweak("gpu.nvidia.write-combining.disable", "Disable NVIDIA Write Combining", "Disables two NVIDIA write-combining paths. Adamx exposes this for systems where it improves frame pacing, but it can also reduce transfer throughput on other drivers. Benchmark before keeping it.", TweakCategory.Gpu, TweakRiskLevel.High, list, requiresAdministrator: true, requiresRestart: true);
	}

	private TweakDefinition BuildNvidiaPreemptionTweak()
	{
		return BuildRegistryDwordSetTweak("gpu.nvidia.preemption.disable", "Disable NVIDIA GPU Preemption", "Prevents several NVIDIA graphics, compute, copy-engine, and mid-buffer preemption paths from interrupting active GPU work. It may tighten frame pacing, but can hurt multitasking, CUDA workloads, or driver recovery, so this is an opt-in high-risk tweak.", TweakCategory.Gpu, TweakRiskLevel.High, new RegistryDwordTarget[10]
		{
			new RegistryDwordTarget(Registry.LocalMachine, "HKLM\\GraphicsDrivers\\Scheduler:EnablePreemption", "SYSTEM\\CurrentControlSet\\Control\\GraphicsDrivers\\Scheduler", "EnablePreemption", 0),
			new RegistryDwordTarget(Registry.LocalMachine, "HKLM\\nvlddmkm:DisablePreemption", "SYSTEM\\CurrentControlSet\\Services\\nvlddmkm", "DisablePreemption", 1),
			new RegistryDwordTarget(Registry.LocalMachine, "HKLM\\nvlddmkm:EnableSCGMidBufferPreemption", "SYSTEM\\CurrentControlSet\\Services\\nvlddmkm", "EnableSCGMidBufferPreemption", 0),
			new RegistryDwordTarget(Registry.LocalMachine, "HKLM\\nvlddmkm:EnableMidGfxPreemptionVGPU", "SYSTEM\\CurrentControlSet\\Services\\nvlddmkm", "EnableMidGfxPreemptionVGPU", 0),
			new RegistryDwordTarget(Registry.LocalMachine, "HKLM\\nvlddmkm:EnableMidGfxPreemption", "SYSTEM\\CurrentControlSet\\Services\\nvlddmkm", "EnableMidGfxPreemption", 0),
			new RegistryDwordTarget(Registry.LocalMachine, "HKLM\\nvlddmkm:EnableMidBufferPreemption", "SYSTEM\\CurrentControlSet\\Services\\nvlddmkm", "EnableMidBufferPreemption", 0),
			new RegistryDwordTarget(Registry.LocalMachine, "HKLM\\nvlddmkm:EnableCEPreemption", "SYSTEM\\CurrentControlSet\\Services\\nvlddmkm", "EnableCEPreemption", 0),
			new RegistryDwordTarget(Registry.LocalMachine, "HKLM\\nvlddmkm:EnableAsyncMidBufferPreemption", "SYSTEM\\CurrentControlSet\\Services\\nvlddmkm", "EnableAsyncMidBufferPreemption", 0),
			new RegistryDwordTarget(Registry.LocalMachine, "HKLM\\nvlddmkm:DisablePreemptionOnS3S4", "SYSTEM\\CurrentControlSet\\Services\\nvlddmkm", "DisablePreemptionOnS3S4", 1),
			new RegistryDwordTarget(Registry.LocalMachine, "HKLM\\nvlddmkm:DisableCudaContextPreemption", "SYSTEM\\CurrentControlSet\\Services\\nvlddmkm", "DisableCudaContextPreemption", 1)
		}, requiresAdministrator: true, requiresRestart: true);
	}

	private TweakDefinition BuildNvidiaHiddenPowerSavingTweak()
	{
		return BuildRegistryDwordSetTweak("gpu.nvidia.hidden-power-saving.disable", "Disable Hidden NVIDIA Power Saving", "Disables asynchronous P-state and PCIe/display power-saving behavior in every detected NVIDIA display-adapter registry key. Intended for plugged-in desktops; it increases idle power use and can worsen thermal throttling on laptops.", TweakCategory.Gpu, TweakRiskLevel.Medium, BuildGpuClassDwordTargets(GpuVendor.Nvidia, ("DisableAsyncPStates", 1), ("PCIEPowerControl", 0), ("PowerSaverHsyncOn", 0)), requiresAdministrator: true, requiresRestart: true);
	}

	private TweakDefinition BuildNvidiaP0StateTweak()
	{
		return BuildRegistryDwordSetTweak("gpu.nvidia.p0-state.enable", "Prefer NVIDIA P0 Performance State", "Disables dynamic GPU P-state switching so the NVIDIA driver favors its maximum-performance state. This can remove clock-ramp latency, but substantially increases idle power, heat, and fan activity; use only after benchmarking temperatures.", TweakCategory.Gpu, TweakRiskLevel.High, BuildGpuClassDwordTargets(GpuVendor.Nvidia, ("DisableDynamicPstate", 1)), requiresAdministrator: true, requiresRestart: true);
	}

	private TweakDefinition BuildNvidiaClockGatingTweak()
	{
		return BuildRegistryDwordSetTweak("gpu.nvidia.clock-gating.disable", "Disable NVIDIA Clock Gating", "Disables NVIDIA engine-level clock gating across the detected display adapters. It can remove wake-up latency, but increases heat and power draw and is driver-dependent. This high-risk control is never included in automatic optimization.", TweakCategory.Gpu, TweakRiskLevel.High, BuildGpuClassDwordTargets(GpuVendor.Nvidia, ("RMElcg", 1431655765)), requiresAdministrator: true, requiresRestart: true);
	}

	private TweakDefinition BuildAmdPreemptionTweak()
	{
		return BuildRegistryDwordSetTweak("gpu.amd.preemption.disable", "Disable Radeon GPU Preemption", "Disables Radeon graphics, compute, and SDMA preemption for uninterrupted GPU work. It may improve frame consistency on some drivers but can hurt multitasking and driver recovery, so it is an opt-in high-risk setting.", TweakCategory.Gpu, TweakRiskLevel.High, BuildGpuClassDwordTargets(GpuVendor.Amd, ("KMD_EnableGraphicsPreemption", 0), ("KMD_EnableComputePreemption", 0), ("KMD_EnableSDMAPreemption", 0)), requiresAdministrator: true, requiresRestart: true);
	}

	private TweakDefinition BuildAmdDmaCopyTweak()
	{
		return BuildRegistryDwordSetTweak("gpu.amd.dma-copy.disable", "Disable Radeon DMA Copy", "Routes Radeon GPU memory copies away from the dedicated DMA copy path. This mirrors the Adamx option, but results vary by GPU generation and workload; benchmark and restore it if transfer performance or stability gets worse.", TweakCategory.Gpu, TweakRiskLevel.High, BuildGpuClassDwordTargets(GpuVendor.Amd, ("DisableDMACopy", 1)), requiresAdministrator: true, requiresRestart: true);
	}

	private TweakDefinition BuildAmdBlockWriteTweak()
	{
		return BuildRegistryDwordSetTweak("gpu.amd.block-write.enable", "Enable Radeon Block Write", "Enables Radeon block-write memory transactions by clearing the driver's DisableBlockWrite flag. It can improve VRAM write efficiency on supported drivers and is fully reversible.", TweakCategory.Gpu, TweakRiskLevel.Medium, BuildGpuClassDwordTargets(GpuVendor.Amd, ("DisableBlockWrite", 0)), requiresAdministrator: true, requiresRestart: true);
	}

	private TweakDefinition BuildAmdStutterModeTweak()
	{
		return BuildRegistryDwordSetTweak("gpu.amd.stutter-mode.disable", "Disable Radeon Stutter Mode", "Turns off the Radeon driver's stutter-mode power behavior so clocks are not deliberately reduced between short bursts of work. This can increase idle power use.", TweakCategory.Gpu, TweakRiskLevel.Medium, BuildGpuClassDwordTargets(GpuVendor.Amd, ("StutterMode", 0)), requiresAdministrator: true, requiresRestart: true);
	}

	private TweakDefinition BuildAmdUlpsTweak()
	{
		return BuildRegistryDwordSetTweak("gpu.amd.ulps.disable", "Disable Radeon ULPS", "Disables Ultra Low Power State on every detected Radeon adapter. This can prevent idle-clock wake stutter and multi-GPU sleep issues, at the cost of increased idle power consumption.", TweakCategory.Gpu, TweakRiskLevel.Medium, BuildGpuClassDwordTargets(GpuVendor.Amd, ("EnableUlps", 0)), requiresAdministrator: true, requiresRestart: true);
	}

	private TweakDefinition BuildAmdSclkDeepSleepTweak()
	{
		return BuildRegistryDwordSetTweak("gpu.amd.sclk-deep-sleep.disable", "Disable Radeon SCLK Deep Sleep", "Keeps the Radeon shader clock out of its deepest sleep state to reduce clock wake-up latency. Recommended only for plugged-in desktops with adequate cooling.", TweakCategory.Gpu, TweakRiskLevel.Medium, BuildGpuClassDwordTargets(GpuVendor.Amd, ("PP_SclkDeepSleepDisable", 1)), requiresAdministrator: true, requiresRestart: true);
	}

	private TweakDefinition BuildAmdLightSleepTweak()
	{
		return BuildRegistryDwordSetTweak("gpu.amd.light-sleep.disable", "Disable Radeon Light Sleep", "Disables the Radeon driver's lighter idle-sleep mode so the GPU remains ready between frames. It may reduce wake latency while increasing idle heat and power use.", TweakCategory.Gpu, TweakRiskLevel.Medium, BuildGpuClassDwordTargets(GpuVendor.Amd, ("PP_DisableLightSleep", 1)), requiresAdministrator: true, requiresRestart: true);
	}

	private TweakDefinition BuildAmdGpuPowerDownTweak()
	{
		return BuildRegistryDwordSetTweak("gpu.amd.power-down.disable", "Disable Radeon GPU Power Down", "Stops the Radeon driver from powering down GPU blocks while idle. This can remove the first-draw wake-up cost but increases idle consumption and should be tested on your hardware.", TweakCategory.Gpu, TweakRiskLevel.Medium, BuildGpuClassDwordTargets(GpuVendor.Amd, ("PP_GPUPowerDownEnabled", 0)), requiresAdministrator: true, requiresRestart: true);
	}

	private TweakDefinition BuildAmdGpioPowerSavingTweak()
	{
		return BuildRegistryDwordSetTweak("gpu.amd.gpio-power-saving.disable", "Disable Radeon GPIO Power Saving", "Keeps Radeon GPIO control lines active instead of placing them in a power-save mode. The trade-off is slightly higher idle power usage.", TweakCategory.Gpu, TweakRiskLevel.Medium, BuildGpuClassDwordTargets(GpuVendor.Amd, ("GCOOPTION_DisableGPIOPowerSaveMode", 1)), requiresAdministrator: true, requiresRestart: true);
	}

	private TweakDefinition BuildAmdPowerGatingTweak()
	{
		return BuildRegistryDwordSetTweak("gpu.amd.power-gating.disable", "Disable Radeon Power Gating", "Disables general and DRM/DMA power gating in the Radeon driver. This may remove engine wake-up latency, but materially increases heat and power use and is never auto-applied.", TweakCategory.Gpu, TweakRiskLevel.High, BuildGpuClassDwordTargets(GpuVendor.Amd, ("DisablePowerGating", 1), ("DisableDrmdmaPowerGating", 1)), requiresAdministrator: true, requiresRestart: true);
	}

	private TweakDefinition BuildAmdClockGatingTweak()
	{
		return BuildRegistryDwordSetTweak("gpu.amd.clock-gating.disable", "Disable Radeon Clock Gating", "Disables general, VCE, and UVD clock gating across detected Radeon adapters. This is a driver-dependent, high-power tweak intended only for benchmarked desktop configurations.", TweakCategory.Gpu, TweakRiskLevel.High, BuildGpuClassDwordTargets(GpuVendor.Amd, ("DisableAllClockGating", 1), ("EnableVceSwClockGating", 0), ("EnableUvdClockGating", 0)), requiresAdministrator: true, requiresRestart: true);
	}

	private TweakDefinition BuildGameBarCaptureTweak()
	{
		TweakDefinition tweakDefinition = new TweakDefinition();
		tweakDefinition.Id = "gaming.game-bar-capture.disable";
		tweakDefinition.Name = "Disable Game Bar Background Recording";
		tweakDefinition.Description = "Stops Xbox Game Bar from continuously capturing background gameplay footage for its \"record what happened\" feature, which uses CPU, GPU, and disk in the background even when you never open it. Game Bar itself (Win+G) still opens normally.";
		tweakDefinition.Category = TweakCategory.Gaming;
		tweakDefinition.RiskLevel = TweakRiskLevel.Low;
		tweakDefinition.RequiresAdministrator = false;
		tweakDefinition.RequiresRestart = false;
		tweakDefinition.AffectedSettings = new string[2] { "HKCU\\GameConfigStore:GameDVR_Enabled", "HKCU\\GameDVR:AppCaptureEnabled" };
		tweakDefinition.CurrentStateReader = () => Task.FromResult((RegistryHelper.ReadDword(Registry.CurrentUser, "System\\GameConfigStore", "GameDVR_Enabled") == 0 && RegistryHelper.ReadDword(Registry.CurrentUser, "Software\\Microsoft\\Windows\\CurrentVersion\\GameDVR", "AppCaptureEnabled") == 0) ? TweakState.Applied : TweakState.NotApplied);
		tweakDefinition.ApplyAction = delegate
		{
			int? num = RegistryHelper.ReadDword(Registry.CurrentUser, "System\\GameConfigStore", "GameDVR_Enabled");
			int? num2 = RegistryHelper.ReadDword(Registry.CurrentUser, "Software\\Microsoft\\Windows\\CurrentVersion\\GameDVR", "AppCaptureEnabled");
			_restore.SaveBackup("gaming.game-bar-capture.disable", (num?.ToString() ?? "null") + "|" + (num2?.ToString() ?? "null"));
			RegistryHelper.WriteDword(Registry.CurrentUser, "System\\GameConfigStore", "GameDVR_Enabled", 0);
			RegistryHelper.WriteDword(Registry.CurrentUser, "Software\\Microsoft\\Windows\\CurrentVersion\\GameDVR", "AppCaptureEnabled", 0);
			return Task.FromResult(TweakActionResult.Ok("Game Bar background recording disabled."));
		};
		tweakDefinition.RestoreAction = delegate
		{
			string backup = _restore.GetBackup("gaming.game-bar-capture.disable");
			if (backup == null)
			{
				return Task.FromResult(TweakActionResult.Fail("No previous Game DVR setting was recorded."));
			}
			string[] array = backup.Split('|');
			RestoreOptionalDword(Registry.CurrentUser, "System\\GameConfigStore", "GameDVR_Enabled", array[0]);
			RestoreOptionalDword(Registry.CurrentUser, "Software\\Microsoft\\Windows\\CurrentVersion\\GameDVR", "AppCaptureEnabled", (array.Length > 1) ? array[1] : "null");
			_restore.ClearBackup("gaming.game-bar-capture.disable");
			return Task.FromResult(TweakActionResult.Ok("Game Bar background recording setting restored."));
		};
		return tweakDefinition;
	}

	private TweakDefinition BuildMmcssPriorityTweak() => MmcssSchedulingTweaks.Games(_restore);

	private TweakDefinition BuildShowFileExtensionsTweak()
	{
		TweakDefinition tweakDefinition = new TweakDefinition();
		tweakDefinition.Id = "windows.show-file-extensions.enable";
		tweakDefinition.Name = "Show File Extensions";
		tweakDefinition.Description = "Displays file extensions (like .exe, .zip) in File Explorer instead of hiding them, making it easier to identify file types at a glance. Takes effect in newly opened Explorer windows.";
		tweakDefinition.Category = TweakCategory.Windows;
		tweakDefinition.RiskLevel = TweakRiskLevel.Low;
		tweakDefinition.RequiresAdministrator = false;
		tweakDefinition.RequiresRestart = false;
		tweakDefinition.AffectedSettings = new string[1] { "HKCU\\Explorer\\Advanced:HideFileExt" };
		tweakDefinition.CurrentStateReader = () => Task.FromResult((RegistryHelper.ReadDword(Registry.CurrentUser, "Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\Advanced", "HideFileExt") == 0) ? TweakState.Applied : TweakState.NotApplied);
		tweakDefinition.ApplyAction = delegate
		{
			int num = RegistryHelper.ReadDword(Registry.CurrentUser, "Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\Advanced", "HideFileExt") ?? 1;
			_restore.SaveBackup("windows.show-file-extensions.enable", num.ToString());
			RegistryHelper.WriteDword(Registry.CurrentUser, "Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\Advanced", "HideFileExt", 0);
			return Task.FromResult(TweakActionResult.Ok("File extensions will now show in new Explorer windows."));
		};
		tweakDefinition.RestoreAction = delegate
		{
			int result;
			int value = ((!int.TryParse(_restore.GetBackup("windows.show-file-extensions.enable"), out result)) ? 1 : result);
			RegistryHelper.WriteDword(Registry.CurrentUser, "Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\Advanced", "HideFileExt", value);
			_restore.ClearBackup("windows.show-file-extensions.enable");
			return Task.FromResult(TweakActionResult.Ok("File extension visibility restored."));
		};
		return tweakDefinition;
	}

	private TweakDefinition BuildSearchWebSuggestionsTweak()
	{
		TweakDefinition tweakDefinition = new TweakDefinition();
		tweakDefinition.Id = "windows.search-web-suggestions.disable";
		tweakDefinition.Name = "Disable Start Menu Web Search Suggestions";
		tweakDefinition.Description = "Stops Windows Search from sending what you type in the Start menu to Bing for web suggestions, keeping search local to your PC. Local app and file search is unaffected.";
		tweakDefinition.Category = TweakCategory.Windows;
		tweakDefinition.RiskLevel = TweakRiskLevel.Low;
		tweakDefinition.RequiresAdministrator = false;
		tweakDefinition.RequiresRestart = false;
		tweakDefinition.AffectedSettings = new string[1] { "HKCU\\Policies\\Explorer:DisableSearchBoxSuggestions" };
		tweakDefinition.CurrentStateReader = () => Task.FromResult((RegistryHelper.ReadDword(Registry.CurrentUser, "Software\\Policies\\Microsoft\\Windows\\Explorer", "DisableSearchBoxSuggestions") == 1) ? TweakState.Applied : TweakState.NotApplied);
		tweakDefinition.ApplyAction = delegate
		{
			int? num = RegistryHelper.ReadDword(Registry.CurrentUser, "Software\\Policies\\Microsoft\\Windows\\Explorer", "DisableSearchBoxSuggestions");
			_restore.SaveBackup("windows.search-web-suggestions.disable", num?.ToString() ?? "null");
			RegistryHelper.WriteDword(Registry.CurrentUser, "Software\\Policies\\Microsoft\\Windows\\Explorer", "DisableSearchBoxSuggestions", 1);
			return Task.FromResult(TweakActionResult.Ok("Start menu web search suggestions disabled."));
		};
		tweakDefinition.RestoreAction = delegate
		{
			string backup = _restore.GetBackup("windows.search-web-suggestions.disable");
			int result;
			if (backup == null || backup == "null")
			{
				RegistryHelper.DeleteValue(Registry.CurrentUser, "Software\\Policies\\Microsoft\\Windows\\Explorer", "DisableSearchBoxSuggestions");
			}
			else if (int.TryParse(backup, out result))
			{
				RegistryHelper.WriteDword(Registry.CurrentUser, "Software\\Policies\\Microsoft\\Windows\\Explorer", "DisableSearchBoxSuggestions", result);
			}
			_restore.ClearBackup("windows.search-web-suggestions.disable");
			return Task.FromResult(TweakActionResult.Ok("Search suggestion setting restored."));
		};
		return tweakDefinition;
	}

	private TweakDefinition BuildWindowsTipsTweak()
	{
		TweakDefinition tweakDefinition = new TweakDefinition();
		tweakDefinition.Id = "windows.tips-suggestions.disable";
		tweakDefinition.Name = "Disable Windows Tips & Suggestions";
		tweakDefinition.Description = "Turns off the suggested content and tips Windows shows in Settings, the Start menu, and the lock screen. Purely cosmetic - no functionality is removed.";
		tweakDefinition.Category = TweakCategory.Windows;
		tweakDefinition.RiskLevel = TweakRiskLevel.Low;
		tweakDefinition.RequiresAdministrator = false;
		tweakDefinition.RequiresRestart = false;
		tweakDefinition.AffectedSettings = new string[1] { "HKCU\\ContentDeliveryManager:SubscribedContent-338389Enabled" };
		tweakDefinition.CurrentStateReader = () => Task.FromResult((RegistryHelper.ReadDword(Registry.CurrentUser, "Software\\Microsoft\\Windows\\CurrentVersion\\ContentDeliveryManager", "SubscribedContent-338389Enabled") == 0) ? TweakState.Applied : TweakState.NotApplied);
		tweakDefinition.ApplyAction = delegate
		{
			int num = RegistryHelper.ReadDword(Registry.CurrentUser, "Software\\Microsoft\\Windows\\CurrentVersion\\ContentDeliveryManager", "SubscribedContent-338389Enabled") ?? 1;
			_restore.SaveBackup("windows.tips-suggestions.disable", num.ToString());
			RegistryHelper.WriteDword(Registry.CurrentUser, "Software\\Microsoft\\Windows\\CurrentVersion\\ContentDeliveryManager", "SubscribedContent-338389Enabled", 0);
			return Task.FromResult(TweakActionResult.Ok("Windows tips and suggestions disabled."));
		};
		tweakDefinition.RestoreAction = delegate
		{
			int result;
			int value = ((!int.TryParse(_restore.GetBackup("windows.tips-suggestions.disable"), out result)) ? 1 : result);
			RegistryHelper.WriteDword(Registry.CurrentUser, "Software\\Microsoft\\Windows\\CurrentVersion\\ContentDeliveryManager", "SubscribedContent-338389Enabled", value);
			_restore.ClearBackup("windows.tips-suggestions.disable");
			return Task.FromResult(TweakActionResult.Ok("Tips and suggestions setting restored."));
		};
		return tweakDefinition;
	}

	private TweakDefinition BuildFastStartupTweak()
	{
		TweakDefinition tweakDefinition = new TweakDefinition();
		tweakDefinition.Id = "windows.fast-startup.disable";
		tweakDefinition.Name = "Disable Fast Startup";
		tweakDefinition.Description = "Turns off Windows' hybrid boot (Fast Startup), which saves kernel session state to disk on shutdown to speed up the next boot. Disabling it makes shutdown a true full shutdown - useful if you dual-boot, use certain external drives, or install driver/BIOS updates that expect a clean boot. Takes effect on your next shutdown; boot time may increase slightly afterward.";
		tweakDefinition.Category = TweakCategory.Windows;
		tweakDefinition.RiskLevel = TweakRiskLevel.Medium;
		tweakDefinition.RequiresAdministrator = true;
		tweakDefinition.RequiresRestart = false;
		tweakDefinition.AffectedSettings = new string[1] { "HKLM\\Power:HiberbootEnabled" };
		tweakDefinition.CurrentStateReader = () => Task.FromResult((RegistryHelper.ReadDword(Registry.LocalMachine, "SYSTEM\\CurrentControlSet\\Control\\Session Manager\\Power", "HiberbootEnabled") == 0) ? TweakState.Applied : TweakState.NotApplied);
		tweakDefinition.ApplyAction = delegate
		{
			int num = RegistryHelper.ReadDword(Registry.LocalMachine, "SYSTEM\\CurrentControlSet\\Control\\Session Manager\\Power", "HiberbootEnabled") ?? 1;
			_restore.SaveBackup("windows.fast-startup.disable", num.ToString());
			RegistryHelper.WriteDword(Registry.LocalMachine, "SYSTEM\\CurrentControlSet\\Control\\Session Manager\\Power", "HiberbootEnabled", 0);
			return Task.FromResult(TweakActionResult.Ok("Fast Startup disabled. Takes effect on your next shutdown."));
		};
		tweakDefinition.RestoreAction = delegate
		{
			int result;
			int value = ((!int.TryParse(_restore.GetBackup("windows.fast-startup.disable"), out result)) ? 1 : result);
			RegistryHelper.WriteDword(Registry.LocalMachine, "SYSTEM\\CurrentControlSet\\Control\\Session Manager\\Power", "HiberbootEnabled", value);
			_restore.ClearBackup("windows.fast-startup.disable");
			return Task.FromResult(TweakActionResult.Ok("Fast Startup setting restored."));
		};
		return tweakDefinition;
	}

	private TweakDefinition BuildNagleAlgorithmTweak()
	{
		TweakDefinition tweakDefinition = new TweakDefinition();
		tweakDefinition.Id = "advanced.nagle-algorithm.disable";
		tweakDefinition.Name = "Disable Nagle's Algorithm";
		tweakDefinition.Description = "Configures your currently active network adapter to send small packets immediately instead of briefly buffering and combining them, which can reduce round-trip latency in latency-sensitive applications. Trade-off: slightly less efficient for large bulk transfers. Applies only to the adapter that's active right now - a different network won't carry this over.";
		tweakDefinition.Category = TweakCategory.Network;
		tweakDefinition.RiskLevel = TweakRiskLevel.Medium;
		tweakDefinition.RequiresAdministrator = true;
		tweakDefinition.RequiresRestart = false;
		tweakDefinition.AffectedSettings = new string[2] { "HKLM\\Tcpip\\Interfaces\\*:TcpAckFrequency", "HKLM\\Tcpip\\Interfaces\\*:TCPNoDelay" };
		tweakDefinition.CurrentStateReader = delegate
		{
			NetworkInterface activeAdapter = GetActiveAdapter();
			if (activeAdapter == null)
			{
				return Task.FromResult(TweakState.Unsupported);
			}
			string subKeyPath = "SYSTEM\\CurrentControlSet\\Services\\Tcpip\\Parameters\\Interfaces\\" + activeAdapter.Id;
			int? num = RegistryHelper.ReadDword(Registry.LocalMachine, subKeyPath, "TcpAckFrequency");
			int? num2 = RegistryHelper.ReadDword(Registry.LocalMachine, subKeyPath, "TCPNoDelay");
			return Task.FromResult((num == 1 && num2 == 1) ? TweakState.Applied : TweakState.NotApplied);
		};
		tweakDefinition.ApplyAction = delegate
		{
			NetworkInterface activeAdapter = GetActiveAdapter();
			if (activeAdapter == null)
			{
				return Task.FromResult(TweakActionResult.Fail("No active network adapter was found."));
			}
			string subKeyPath = "SYSTEM\\CurrentControlSet\\Services\\Tcpip\\Parameters\\Interfaces\\" + activeAdapter.Id;
			int? num = RegistryHelper.ReadDword(Registry.LocalMachine, subKeyPath, "TcpAckFrequency");
			int? num2 = RegistryHelper.ReadDword(Registry.LocalMachine, subKeyPath, "TCPNoDelay");
			_restore.SaveBackup("advanced.nagle-algorithm.disable", $"{num?.ToString() ?? "null"}|{num2?.ToString() ?? "null"}|{activeAdapter.Id}");
			RegistryHelper.WriteDword(Registry.LocalMachine, subKeyPath, "TcpAckFrequency", 1);
			RegistryHelper.WriteDword(Registry.LocalMachine, subKeyPath, "TCPNoDelay", 1);
			return Task.FromResult(TweakActionResult.Ok("Nagle's algorithm disabled on " + activeAdapter.Name + "."));
		};
		tweakDefinition.RestoreAction = delegate
		{
			string[] array = _restore.GetBackup("advanced.nagle-algorithm.disable")?.Split('|') ?? Array.Empty<string>();
			if (array.Length != 3)
			{
				return Task.FromResult(TweakActionResult.Fail("No previous adapter setting was recorded."));
			}
			string subKeyPath = "SYSTEM\\CurrentControlSet\\Services\\Tcpip\\Parameters\\Interfaces\\" + array[2];
			int result;
			if (array[0] == "null")
			{
				RegistryHelper.DeleteValue(Registry.LocalMachine, subKeyPath, "TcpAckFrequency");
			}
			else if (int.TryParse(array[0], out result))
			{
				RegistryHelper.WriteDword(Registry.LocalMachine, subKeyPath, "TcpAckFrequency", result);
			}
			int result2;
			if (array[1] == "null")
			{
				RegistryHelper.DeleteValue(Registry.LocalMachine, subKeyPath, "TCPNoDelay");
			}
			else if (int.TryParse(array[1], out result2))
			{
				RegistryHelper.WriteDword(Registry.LocalMachine, subKeyPath, "TCPNoDelay", result2);
			}
			_restore.ClearBackup("advanced.nagle-algorithm.disable");
			return Task.FromResult(TweakActionResult.Ok("Nagle's algorithm setting restored."));
		};
		return tweakDefinition;
	}

	private TweakDefinition BuildSysMainServiceTweak()
	{
		TweakDefinition tweakDefinition = new TweakDefinition();
		tweakDefinition.Id = "advanced.sysmain-service.disable";
		tweakDefinition.Name = "Disable SysMain (Superfetch) Service";
		tweakDefinition.Description = "Stops and disables the SysMain service, which preloads frequently used applications into RAM to speed up launches - mainly useful on traditional hard drives. On SSD-based systems it mostly adds background disk activity for little benefit. App launches may be marginally slower after disabling on HDD systems.";
		tweakDefinition.Category = TweakCategory.Storage;
		tweakDefinition.RiskLevel = TweakRiskLevel.Medium;
		tweakDefinition.RequiresAdministrator = true;
		tweakDefinition.RequiresRestart = false;
		tweakDefinition.AffectedSettings = new string[1] { "service:SysMain" };
		tweakDefinition.CurrentStateReader = async delegate
		{
			int? num = await GetServiceStartTypeAsync("SysMain");
			return num.HasValue ? ((num == 4) ? TweakState.Applied : TweakState.NotApplied) : TweakState.Unknown;
		};
		tweakDefinition.ApplyAction = async delegate
		{
			int? num = await GetServiceStartTypeAsync("SysMain");
			_restore.SaveBackup("advanced.sysmain-service.disable", num?.ToString() ?? "2");
			await RunProcessAsync("sc", "config SysMain start= disabled");
			await RunProcessAsync("sc", "stop SysMain");
			return TweakActionResult.Ok("SysMain service disabled.");
		};
		tweakDefinition.RestoreAction = async delegate
		{
			int result;
			int code = (int.TryParse(_restore.GetBackup("advanced.sysmain-service.disable"), out result) ? result : 2);
			string keyword = MapStartTypeToScKeyword(code);
			await RunProcessAsync("sc", "config SysMain start= " + keyword);
			if (keyword != "disabled")
			{
				await RunProcessAsync("sc", "start SysMain");
			}
			_restore.ClearBackup("advanced.sysmain-service.disable");
			return TweakActionResult.Ok("SysMain service setting restored.");
		};
		return tweakDefinition;
	}

	private TweakDefinition BuildSearchIndexingServiceTweak()
	{
		TweakDefinition tweakDefinition = new TweakDefinition();
		tweakDefinition.Id = "advanced.search-indexing-service.disable";
		tweakDefinition.Name = "Disable Windows Search Indexing";
		tweakDefinition.Description = "Stops and disables the Windows Search (WSearch) service, which continuously indexes your files in the background to make Start menu and File Explorer search fast. Disabling it reduces background disk activity, but searching for files and apps will become noticeably slower since Windows has to search on demand instead.";
		tweakDefinition.Category = TweakCategory.Storage;
		tweakDefinition.RiskLevel = TweakRiskLevel.Medium;
		tweakDefinition.RequiresAdministrator = true;
		tweakDefinition.RequiresRestart = false;
		tweakDefinition.AffectedSettings = new string[1] { "service:WSearch" };
		tweakDefinition.CurrentStateReader = async delegate
		{
			int? num = await GetServiceStartTypeAsync("WSearch");
			return num.HasValue ? ((num == 4) ? TweakState.Applied : TweakState.NotApplied) : TweakState.Unknown;
		};
		tweakDefinition.ApplyAction = async delegate
		{
			int? num = await GetServiceStartTypeAsync("WSearch");
			_restore.SaveBackup("advanced.search-indexing-service.disable", num?.ToString() ?? "2");
			await RunProcessAsync("sc", "config WSearch start= disabled");
			await RunProcessAsync("sc", "stop WSearch");
			return TweakActionResult.Ok("Windows Search indexing disabled.");
		};
		tweakDefinition.RestoreAction = async delegate
		{
			int result;
			int code = (int.TryParse(_restore.GetBackup("advanced.search-indexing-service.disable"), out result) ? result : 2);
			string keyword = MapStartTypeToScKeyword(code);
			await RunProcessAsync("sc", "config WSearch start= " + keyword);
			if (keyword != "disabled")
			{
				await RunProcessAsync("sc", "start WSearch");
			}
			_restore.ClearBackup("advanced.search-indexing-service.disable");
			return TweakActionResult.Ok("Windows Search indexing setting restored.");
		};
		return tweakDefinition;
	}

	private TweakDefinition BuildRotatingLockScreenTweak()
	{
		return BuildRegistryDwordSetTweak("windows.rotating-lock-screen.disable", "Disable Rotating Lock-Screen Content", "Stops Windows Spotlight from rotating lock-screen images and placing promotional overlay content on the lock screen. The sign-in screen and password/PIN protection remain enabled.", TweakCategory.Windows, TweakRiskLevel.Low, new RegistryDwordTarget[2]
		{
			new RegistryDwordTarget(Registry.CurrentUser, "HKCU\\ContentDeliveryManager:RotatingLockScreenEnabled", "Software\\Microsoft\\Windows\\CurrentVersion\\ContentDeliveryManager", "RotatingLockScreenEnabled", 0),
			new RegistryDwordTarget(Registry.CurrentUser, "HKCU\\ContentDeliveryManager:RotatingLockScreenOverlayEnabled", "Software\\Microsoft\\Windows\\CurrentVersion\\ContentDeliveryManager", "RotatingLockScreenOverlayEnabled", 0)
		});
	}

	private TweakDefinition BuildNetworkThrottlingTweak()
	{
		TweakDefinition tweakDefinition = new TweakDefinition();
		tweakDefinition.Id = "network.throttling-index.disable";
		tweakDefinition.Name = "Disable Network Throttling Index";
		tweakDefinition.Description = "Windows caps non-multimedia network processing by default (via MMCSS) to keep audio/video streaming smooth. Removing that cap can reduce network-related stutter in games and downloads, at the cost of the smoothing behavior it was originally added for.";
		tweakDefinition.Category = TweakCategory.Network;
		tweakDefinition.RiskLevel = TweakRiskLevel.Medium;
		tweakDefinition.RequiresAdministrator = true;
		tweakDefinition.RequiresRestart = false;
		tweakDefinition.AffectedSettings = new string[1] { "HKLM\\Multimedia\\SystemProfile:NetworkThrottlingIndex" };
		tweakDefinition.CurrentStateReader = () => Task.FromResult((RegistryHelper.ReadDword(Registry.LocalMachine, "SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion\\Multimedia\\SystemProfile", "NetworkThrottlingIndex") == -1) ? TweakState.Applied : TweakState.NotApplied);
		tweakDefinition.ApplyAction = delegate
		{
			int? num = RegistryHelper.ReadDword(Registry.LocalMachine, "SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion\\Multimedia\\SystemProfile", "NetworkThrottlingIndex");
			_restore.SaveBackup("network.throttling-index.disable", num?.ToString() ?? "null");
			RegistryHelper.WriteDword(Registry.LocalMachine, "SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion\\Multimedia\\SystemProfile", "NetworkThrottlingIndex", -1);
			return Task.FromResult(TweakActionResult.Ok("Network throttling index disabled."));
		};
		tweakDefinition.RestoreAction = delegate
		{
			string backup = _restore.GetBackup("network.throttling-index.disable");
			int result;
			if (backup == null || backup == "null")
			{
				RegistryHelper.DeleteValue(Registry.LocalMachine, "SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion\\Multimedia\\SystemProfile", "NetworkThrottlingIndex");
			}
			else if (int.TryParse(backup, out result))
			{
				RegistryHelper.WriteDword(Registry.LocalMachine, "SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion\\Multimedia\\SystemProfile", "NetworkThrottlingIndex", result);
			}
			_restore.ClearBackup("network.throttling-index.disable");
			return Task.FromResult(TweakActionResult.Ok("Network throttling index restored."));
		};
		return tweakDefinition;
	}

	private TweakDefinition BuildDeliveryOptimizationTweak()
	{
		TweakDefinition tweakDefinition = new TweakDefinition();
		tweakDefinition.Id = "network.delivery-optimization.disable";
		tweakDefinition.Name = "Disable Delivery Optimization P2P Sharing";
		tweakDefinition.Description = "Stops Windows Update from uploading and downloading update files to and from other PCs on your network or the internet (peer-to-peer), keeping updates as a direct download from Microsoft only. Reduces background upload bandwidth usage; updates may download marginally slower without the P2P assist.";
		tweakDefinition.Category = TweakCategory.Network;
		tweakDefinition.RiskLevel = TweakRiskLevel.Low;
		tweakDefinition.RequiresAdministrator = true;
		tweakDefinition.RequiresRestart = false;
		tweakDefinition.AffectedSettings = new string[1] { "HKLM\\Policies\\DeliveryOptimization:DODownloadMode" };
		tweakDefinition.CurrentStateReader = () => Task.FromResult((RegistryHelper.ReadDword(Registry.LocalMachine, "SOFTWARE\\Policies\\Microsoft\\Windows\\DeliveryOptimization", "DODownloadMode") == 0) ? TweakState.Applied : TweakState.NotApplied);
		tweakDefinition.ApplyAction = delegate
		{
			int? num = RegistryHelper.ReadDword(Registry.LocalMachine, "SOFTWARE\\Policies\\Microsoft\\Windows\\DeliveryOptimization", "DODownloadMode");
			_restore.SaveBackup("network.delivery-optimization.disable", num?.ToString() ?? "null");
			RegistryHelper.WriteDword(Registry.LocalMachine, "SOFTWARE\\Policies\\Microsoft\\Windows\\DeliveryOptimization", "DODownloadMode", 0);
			return Task.FromResult(TweakActionResult.Ok("Delivery Optimization P2P sharing disabled."));
		};
		tweakDefinition.RestoreAction = delegate
		{
			string backup = _restore.GetBackup("network.delivery-optimization.disable");
			int result;
			if (backup == null || backup == "null")
			{
				RegistryHelper.DeleteValue(Registry.LocalMachine, "SOFTWARE\\Policies\\Microsoft\\Windows\\DeliveryOptimization", "DODownloadMode");
			}
			else if (int.TryParse(backup, out result))
			{
				RegistryHelper.WriteDword(Registry.LocalMachine, "SOFTWARE\\Policies\\Microsoft\\Windows\\DeliveryOptimization", "DODownloadMode", result);
			}
			_restore.ClearBackup("network.delivery-optimization.disable");
			return Task.FromResult(TweakActionResult.Ok("Delivery Optimization setting restored."));
		};
		return tweakDefinition;
	}

	private TweakDefinition BuildSmartDnsResolutionTweak()
	{
		TweakDefinition tweakDefinition = new TweakDefinition();
		tweakDefinition.Id = "network.smart-dns-resolution.disable";
		tweakDefinition.Name = "Disable Smart Multi-Homed Name Resolution";
		tweakDefinition.Description = "Stops Windows from firing DNS queries across every active network adapter at once (VPN, Wi-Fi, Ethernet, etc.) and racing them for the fastest reply. Can reduce inconsistent or leaking DNS lookups when using a VPN, at the cost of Windows' fastest-DNS-wins optimization.";
		tweakDefinition.Category = TweakCategory.Network;
		tweakDefinition.RiskLevel = TweakRiskLevel.Low;
		tweakDefinition.RequiresAdministrator = true;
		tweakDefinition.RequiresRestart = false;
		tweakDefinition.AffectedSettings = new string[1] { "HKLM\\Policies\\DNSClient:DisableSmartNameResolution" };
		tweakDefinition.CurrentStateReader = () => Task.FromResult((RegistryHelper.ReadDword(Registry.LocalMachine, "SOFTWARE\\Policies\\Microsoft\\Windows NT\\DNSClient", "DisableSmartNameResolution") == 1) ? TweakState.Applied : TweakState.NotApplied);
		tweakDefinition.ApplyAction = delegate
		{
			int? num = RegistryHelper.ReadDword(Registry.LocalMachine, "SOFTWARE\\Policies\\Microsoft\\Windows NT\\DNSClient", "DisableSmartNameResolution");
			_restore.SaveBackup("network.smart-dns-resolution.disable", num?.ToString() ?? "null");
			RegistryHelper.WriteDword(Registry.LocalMachine, "SOFTWARE\\Policies\\Microsoft\\Windows NT\\DNSClient", "DisableSmartNameResolution", 1);
			return Task.FromResult(TweakActionResult.Ok("Smart multi-homed name resolution disabled."));
		};
		tweakDefinition.RestoreAction = delegate
		{
			string backup = _restore.GetBackup("network.smart-dns-resolution.disable");
			int result;
			if (backup == null || backup == "null")
			{
				RegistryHelper.DeleteValue(Registry.LocalMachine, "SOFTWARE\\Policies\\Microsoft\\Windows NT\\DNSClient", "DisableSmartNameResolution");
			}
			else if (int.TryParse(backup, out result))
			{
				RegistryHelper.WriteDword(Registry.LocalMachine, "SOFTWARE\\Policies\\Microsoft\\Windows NT\\DNSClient", "DisableSmartNameResolution", result);
			}
			_restore.ClearBackup("network.smart-dns-resolution.disable");
			return Task.FromResult(TweakActionResult.Ok("Smart name resolution setting restored."));
		};
		return tweakDefinition;
	}

	private TweakDefinition BuildTcpHeuristicsTweak()
	{
		TweakDefinition tweakDefinition = new TweakDefinition();
		tweakDefinition.Id = "network.tcp-window-heuristics.disable";
		tweakDefinition.Name = "Disable TCP Window Scaling Heuristics";
		tweakDefinition.Description = "Stops Windows TCP heuristics from overriding the configured receive-window auto-tuning level. This does not turn auto-tuning itself off and is reversible through netsh.";
		tweakDefinition.Category = TweakCategory.Network;
		tweakDefinition.RiskLevel = TweakRiskLevel.Low;
		tweakDefinition.RequiresAdministrator = true;
		tweakDefinition.RequiresRestart = false;
		tweakDefinition.AffectedSettings = new string[1] { "netsh:tcp:window-scaling-heuristics" };
		tweakDefinition.CurrentStateReader = async delegate
		{
			bool? flag = await GetTcpHeuristicsStateAsync();
			return flag.HasValue ? ((flag != true) ? TweakState.Applied : TweakState.NotApplied) : TweakState.Unknown;
		};
		tweakDefinition.ApplyAction = async delegate
		{
			bool? flag = await GetTcpHeuristicsStateAsync();
			if (!flag.HasValue)
			{
				return TweakActionResult.Fail("Windows did not report its TCP heuristics state.");
			}
			_restore.SaveBackup("network.tcp-window-heuristics.disable", flag.Value.ToString());
			return ((await RunProcessAsync("netsh", "int tcp set heuristics disabled")).Item1 == 0) ? TweakActionResult.Ok("TCP window scaling heuristics disabled.") : TweakActionResult.Fail("Windows could not disable TCP window scaling heuristics.");
		};
		tweakDefinition.RestoreAction = async delegate
		{
			if (!bool.TryParse(_restore.GetBackup("network.tcp-window-heuristics.disable"), out var result))
			{
				return TweakActionResult.Fail("No previous TCP heuristics state was recorded.");
			}
			(int, string) obj = await RunProcessAsync("netsh", "int tcp set heuristics " + (result ? "enabled" : "disabled"));
			if (obj.Item1 == 0)
			{
				_restore.ClearBackup("network.tcp-window-heuristics.disable");
			}
			return (obj.Item1 == 0) ? TweakActionResult.Ok("TCP window scaling heuristics restored.") : TweakActionResult.Fail("Windows could not restore TCP window scaling heuristics.");
		};
		return tweakDefinition;
	}

	private TweakDefinition BuildNetBiosTweak()
	{
		TweakDefinition tweakDefinition = new TweakDefinition();
		tweakDefinition.Id = "network.netbios.disable";
		tweakDefinition.Name = "Disable NetBIOS over TCP/IP";
		tweakDefinition.Description = "Disables legacy NetBIOS name resolution on every currently IP-enabled adapter. This can reduce broadcast traffic and attack surface, but may break discovery of old NAS devices or legacy Windows shares.";
		tweakDefinition.Category = TweakCategory.Network;
		tweakDefinition.RiskLevel = TweakRiskLevel.Medium;
		tweakDefinition.RequiresAdministrator = true;
		tweakDefinition.RequiresRestart = true;
		tweakDefinition.AffectedSettings = new string[1] { "wmi:ip-enabled-adapters:TcpipNetbiosOptions" };
		tweakDefinition.CurrentStateReader = async delegate
		{
			List<NetBiosAdapterState> list = await Task.Run((Func<List<NetBiosAdapterState>>)GetNetBiosAdapterStates);
			return (list.Count == 0) ? TweakState.Unsupported : (list.All((NetBiosAdapterState state) => state.Mode == 2) ? TweakState.Applied : TweakState.NotApplied);
		};
		tweakDefinition.ApplyAction = async delegate
		{
			List<NetBiosAdapterState> list = await Task.Run((Func<List<NetBiosAdapterState>>)GetNetBiosAdapterStates);
			if (list.Count == 0)
			{
				return TweakActionResult.Fail("No IP-enabled network adapters were found.");
			}
			_restore.SaveBackup("network.netbios.disable", JsonSerializer.Serialize(list));
			Dictionary<string, uint> targetModes = list.ToDictionary((NetBiosAdapterState state) => state.SettingId, (NetBiosAdapterState _) => 2u);
			return (await Task.Run(() => SetNetBiosModes(targetModes))) ? TweakActionResult.Ok("NetBIOS over TCP/IP disabled on active adapters.") : TweakActionResult.Fail("One or more network adapters rejected the NetBIOS change.");
		};
		tweakDefinition.RestoreAction = async delegate
		{
			string backup = _restore.GetBackup("network.netbios.disable");
			if (backup == null)
			{
				return TweakActionResult.Fail("No previous NetBIOS adapter state was recorded.");
			}
			List<NetBiosAdapterState> list;
			try
			{
				list = JsonSerializer.Deserialize<List<NetBiosAdapterState>>(backup);
			}
			catch (JsonException)
			{
				return TweakActionResult.Fail("The recorded NetBIOS adapter state is invalid.");
			}
			if (list == null || list.Count == 0)
			{
				return TweakActionResult.Fail("The recorded NetBIOS adapter state is empty.");
			}
			Dictionary<string, uint> targetModes = list.ToDictionary((NetBiosAdapterState state) => state.SettingId, (NetBiosAdapterState state) => state.Mode);
			bool num = await Task.Run(() => SetNetBiosModes(targetModes));
			if (num)
			{
				_restore.ClearBackup("network.netbios.disable");
			}
			return num ? TweakActionResult.Ok("NetBIOS adapter settings restored.") : TweakActionResult.Fail("One or more network adapters could not be restored.");
		};
		return tweakDefinition;
	}

	private TweakDefinition BuildMemoryCompressionTweak() => MemoryAgentTweaks.Build(MemoryAgentSetting.MemoryCompression, _restore);

	private TweakDefinition BuildPageCombiningTweak() => MemoryAgentTweaks.Build(MemoryAgentSetting.PageCombining, _restore);

	private TweakDefinition BuildProcessSchedulingTweak()
	{
		return BuildRegistryDwordTweak("advanced.process-scheduling.short-variable-3to1", "Process Scheduling: Short · Variable · 3:1", "Sets Win32PrioritySeparation to 38, explicitly selecting short variable quanta with a 3:1 foreground boost. This matches the normal Windows client behavior but prevents server edition defaults or earlier tuning tools from leaving a throughput-oriented value behind.", TweakCategory.Memory, TweakRiskLevel.Low, Registry.LocalMachine, "HKLM\\PriorityControl:Win32PrioritySeparation", "SYSTEM\\CurrentControlSet\\Control\\PriorityControl", "Win32PrioritySeparation", 38, requiresAdministrator: true, requiresRestart: true);
	}

	private TweakDefinition BuildKeyboardResponseTweak()
	{
		TweakDefinition tweakDefinition = new TweakDefinition();
		tweakDefinition.Id = "input.keyboard-response.fast";
		tweakDefinition.Name = "Fast Keyboard Repeat Response";
		tweakDefinition.Description = "Sets the Windows keyboard repeat delay to its shortest supported value and the repeat rate to its fastest supported value. This changes only the standard keyboard preference exposed by Windows and does not install a filter driver.";
		tweakDefinition.Category = TweakCategory.Input;
		tweakDefinition.RiskLevel = TweakRiskLevel.Low;
		tweakDefinition.RequiresAdministrator = false;
		tweakDefinition.RequiresRestart = false;
		tweakDefinition.AffectedSettings = new string[2] { "user32:keyboard-repeat-delay", "user32:keyboard-repeat-speed" };
		tweakDefinition.CurrentStateReader = () => Task.FromResult((TryGetSystemParameter(22u, out var value) && TryGetSystemParameter(10u, out var value2)) ? ((value == 0 && value2 == 31) ? TweakState.Applied : TweakState.NotApplied) : TweakState.Unsupported);
		tweakDefinition.ApplyAction = delegate
		{
			if (!TryGetSystemParameter(22u, out var value) || !TryGetSystemParameter(10u, out var value2))
			{
				return Task.FromResult(TweakActionResult.Fail("Windows keyboard settings are unavailable."));
			}
			_restore.SaveBackup("input.keyboard-response.fast", $"{value}|{value2}");
			return Task.FromResult((SetSystemParameter(23u, 0u) && SetSystemParameter(11u, 31u)) ? TweakActionResult.Ok("Keyboard repeat response set to fast.") : TweakActionResult.Fail("Windows did not accept the keyboard preference change."));
		};
		tweakDefinition.RestoreAction = delegate
		{
			string[] array = _restore.GetBackup("input.keyboard-response.fast")?.Split('|');
			if (array == null || array.Length != 2 || !uint.TryParse(array[0], out var result) || !uint.TryParse(array[1], out var result2))
			{
				return Task.FromResult(TweakActionResult.Fail("No previous keyboard response was recorded."));
			}
			int num;
			if (SetSystemParameter(23u, result))
			{
				num = (SetSystemParameter(11u, result2) ? 1 : 0);
				if (num != 0)
				{
					_restore.ClearBackup("input.keyboard-response.fast");
				}
			}
			else
			{
				num = 0;
			}
			return Task.FromResult((num != 0) ? TweakActionResult.Ok("Keyboard response restored.") : TweakActionResult.Fail("Windows did not restore the keyboard preference."));
		};
		return tweakDefinition;
	}

	private TweakDefinition BuildTrimTweak()
	{
		TweakDefinition tweakDefinition = new TweakDefinition();
		tweakDefinition.Id = "storage.trim.enable";
		tweakDefinition.Name = "Enable Storage TRIM Notifications";
		tweakDefinition.Description = "Enables Windows delete notifications (TRIM/unmap) for supported storage so the device is told which blocks are no longer in use. Unsupported drives simply ignore the notification; Quantix does not run repeated manual optimization passes.";
		tweakDefinition.Category = TweakCategory.Storage;
		tweakDefinition.RiskLevel = TweakRiskLevel.Low;
		tweakDefinition.RequiresAdministrator = true;
		tweakDefinition.RequiresRestart = false;
		tweakDefinition.AffectedSettings = new string[1] { "fsutil:disable-delete-notify" };
		tweakDefinition.CurrentStateReader = async delegate
		{
			int? num = await GetTrimDisabledStateAsync();
			return (!num.HasValue) ? TweakState.Unsupported : ((num.Value == 0) ? TweakState.Applied : TweakState.NotApplied);
		};
		tweakDefinition.ApplyAction = async delegate
		{
			int? num = await GetTrimDisabledStateAsync();
			if (!num.HasValue)
			{
				return TweakActionResult.Fail("Windows did not report an NTFS TRIM setting.");
			}
			_restore.SaveBackup("storage.trim.enable", num.Value.ToString());
			return ((await RunProcessAsync("fsutil", "behavior set DisableDeleteNotify 0")).Item1 == 0) ? TweakActionResult.Ok("Storage TRIM notifications enabled.") : TweakActionResult.Fail("Windows could not enable TRIM notifications.");
		};
		tweakDefinition.RestoreAction = async delegate
		{
			if (!int.TryParse(_restore.GetBackup("storage.trim.enable"), out var result))
			{
				return TweakActionResult.Fail("No previous TRIM setting was recorded.");
			}
			(int, string) obj = await RunProcessAsync("fsutil", $"behavior set DisableDeleteNotify {result}");
			if (obj.Item1 == 0)
			{
				_restore.ClearBackup("storage.trim.enable");
			}
			return (obj.Item1 == 0) ? TweakActionResult.Ok("Storage TRIM setting restored.") : TweakActionResult.Fail("Windows could not restore the TRIM setting.");
		};
		return tweakDefinition;
	}

	private TweakDefinition BuildTailoredExperiencesTweak()
	{
		return BuildRegistryDwordTweak("windows.tailored-experiences.disable", "Disable Tailored Experiences", "Prevents Windows from using diagnostic data to personalize tips, recommendations, and offers. This uses the documented per-user Cloud Content policy on Windows 10/11 Pro.", TweakCategory.Windows, TweakRiskLevel.Low, Registry.CurrentUser, "HKCU\\Policies\\CloudContent:DisableTailoredExperiencesWithDiagnosticData", "Software\\Policies\\Microsoft\\Windows\\CloudContent", "DisableTailoredExperiencesWithDiagnosticData", 1);
	}

	private TweakDefinition BuildFeedbackNotificationsTweak()
	{
		return BuildRegistryDwordTweak("windows.feedback-notifications.disable", "Disable Windows Feedback Prompts", "Stops Windows Feedback from periodically asking for device feedback. It does not disable error reporting, crash diagnostics, Windows Update, or security telemetry.", TweakCategory.Windows, TweakRiskLevel.Low, Registry.LocalMachine, "HKLM\\Policies\\DataCollection:DoNotShowFeedbackNotifications", "SOFTWARE\\Policies\\Microsoft\\Windows\\DataCollection", "DoNotShowFeedbackNotifications", 1, requiresAdministrator: true);
	}

	private TweakDefinition BuildAdvertisingIdTweak()
	{
		return BuildRegistryDwordTweak("windows.advertising-id.disable", "Disable Windows Advertising ID", "Turns off the Windows advertising identifier through Microsoft's documented policy. Apps can still show non-personalized advertising, but they cannot use this device ID.", TweakCategory.Windows, TweakRiskLevel.Low, Registry.LocalMachine, "HKLM\\Policies\\AdvertisingInfo:DisabledByGroupPolicy", "SOFTWARE\\Policies\\Microsoft\\Windows\\AdvertisingInfo", "DisabledByGroupPolicy", 1, requiresAdministrator: true);
	}

	private TweakDefinition BuildAppLaunchTrackingTweak()
	{
		return BuildRegistryDwordTweak("windows.app-launch-tracking.disable", "Disable App Launch Tracking", "Stops Windows from tracking app launches to improve Start and search suggestions. Disabling it improves privacy but can make frequently used app suggestions less relevant.", TweakCategory.Windows, TweakRiskLevel.Low, Registry.CurrentUser, "HKCU\\Explorer\\Advanced:Start_TrackProgs", "Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\Advanced", "Start_TrackProgs", 0);
	}

	private TweakDefinition BuildAeroShakeTweak()
	{
		return RestrictToPolicyEditions(BuildRegistryDwordTweak("windows.aero-shake.disable", "Disable Aero Shake",
			"Disables the shake-to-minimize mouse gesture through the Windows Explorer policy. Shaking an active window no longer minimizes other windows. Supported on Pro, Enterprise and Education; sign out or restart Explorer for the policy to refresh.",
			TweakCategory.Windows, TweakRiskLevel.Low, Registry.CurrentUser, "HKCU\\Policies\\Explorer:NoWindowMinimizingShortcuts",
			@"Software\Policies\Microsoft\Windows\Explorer", "NoWindowMinimizingShortcuts", 1, requiresRestart: true));
	}

	private TweakDefinition BuildFaultTolerantHeapTweak()
	{
		var tweak = BuildRegistryDwordTweak("windows.fault-tolerant-heap.disable", "Disable Fault Tolerant Heap",
			"Disables Windows Fault Tolerant Heap for new applications after a restart. FTH applies compatibility workarounds to crashing applications; disabling it can make affected software crash again. Existing per-application history is retained. This is not a general FPS guarantee.",
			TweakCategory.Windows, TweakRiskLevel.Medium, Registry.LocalMachine, "HKLM\\Microsoft\\FTH:Enabled", @"SOFTWARE\Microsoft\FTH", "Enabled", 0,
			requiresAdministrator: true, requiresRestart: true);
		var read = tweak.CurrentStateReader; var apply = tweak.ApplyAction;
		bool Supported() => System.IO.File.Exists(System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "fthsvc.dll"));
		tweak.CurrentStateReader = () => Supported() ? read() : Task.FromResult(TweakState.Unsupported);
		tweak.ApplyAction = () => Supported() ? apply() : Task.FromResult(TweakActionResult.Fail("Fault Tolerant Heap is not available on this Windows installation."));
		return tweak;
	}

	private TweakDefinition BuildInsiderBuildsTweak()
	{
		return RestrictToPolicyEditions(BuildRegistryDwordTweak("windows.insider-builds.disable", "Disable Windows Insider",
			"Sets the documented Manage Preview Builds policy to disable Insider preview builds. Supported on Pro, Enterprise and Education. This does not uninstall an existing preview build, roll Windows back, or disable normal security updates. Restart Windows for policy refresh; managed organization policy can take precedence.",
			TweakCategory.Windows, TweakRiskLevel.Medium, Registry.LocalMachine, "HKLM\\WindowsUpdate:ManagePreviewBuildsPolicyValue",
			@"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate", "ManagePreviewBuildsPolicyValue", 0, requiresAdministrator: true, requiresRestart: true));
	}

	private TweakDefinition BuildFastShutdownTweak()
	{
		return BuildRegistryStringSetTweak("windows.fast-shutdown", "Fast Shutdown",
			"Sets the Windows service shutdown wait limit to 2,000 milliseconds. Services may lose unfinished writes if forced to stop too quickly; this is not a guaranteed total shutdown time. Does not suppress application save prompts or change per-service preshutdown handlers. Restore returns the exact original string or removes a previously absent setting. Restart Windows to refresh the setting.",
			TweakCategory.Windows,TweakRiskLevel.High,new[] { new RegistryStringTarget(Registry.LocalMachine,"HKLM\\Control:WaitToKillServiceTimeout",@"SYSTEM\CurrentControlSet\Control","WaitToKillServiceTimeout","2000") },requiresAdministrator:true,requiresRestart:true);
	}

	private TweakDefinition BuildResourcePolicyTweak()
	{
		ulong installed=0;
		bool supported=Environment.OSVersion.Version.Build>=15063 && GetPhysicallyInstalledSystemMemory(out installed) && installed>0 && installed<=uint.MaxValue;
		var tweak=BuildRegistryDwordTweak("windows.resource-policy", "Resource Policy",
			$"Sets the SvcHost split threshold to the installed physical memory size ({installed:N0} KB). Requests grouping of compatible DLL-based services after restart, which can reduce process overhead but reduces service isolation and broadens shared-process failures. Windows keeps some service groups separate. This does not stop services or guarantee more FPS. Restore returns the original threshold.",
			TweakCategory.Windows,TweakRiskLevel.High,Registry.LocalMachine,"HKLM\\Control:SvcHostSplitThresholdInKB",@"SYSTEM\CurrentControlSet\Control","SvcHostSplitThresholdInKB",unchecked((int)installed),requiresAdministrator:true,requiresRestart:true);
		if(!supported)
		{
			tweak.CurrentStateReader=()=>Task.FromResult(TweakState.Unsupported);
			tweak.ApplyAction=()=>Task.FromResult(TweakActionResult.Fail("Installed physical memory could not be represented as a supported service-host threshold."));
		}
		return tweak;
	}

	[DllImport("kernel32.dll",SetLastError=true)]
	[return:MarshalAs(UnmanagedType.Bool)]
	private static extern bool GetPhysicallyInstalledSystemMemory(out ulong memoryKiB);

	private TweakDefinition BuildSingleExplorerProcessTweak()
	{
		return BuildRegistryDwordTweak("windows.explorer.single-process", "Better Alt tab",
			"Turns off Launch folder windows in a separate process in File Explorer. New folder windows use the shared Explorer process rather than requesting a separate host. This does not change the Alt-Tab interface; Explorer tabs and Windows version still determine the final process layout. Sign out or restart Explorer to refresh. Restore returns the original preference.",
			TweakCategory.Windows,TweakRiskLevel.Low,Registry.CurrentUser,"HKCU\\Explorer\\Advanced:SeparateProcess",@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced","SeparateProcess",0,requiresRestart:true);
	}

	private TweakDefinition BuildAudioDuckingTweak()
	{
		return BuildRegistryDwordTweak("windows.audio-ducking.disable", "Disable Communications Audio Ducking", "Prevents Windows from automatically lowering other audio when it detects a voice call. Useful when gaming with voice chat; individual apps can still control their own volume.", TweakCategory.Windows, TweakRiskLevel.Low, Registry.CurrentUser, "HKCU\\Multimedia\\Audio:UserDuckingPreference", "Software\\Microsoft\\Multimedia\\Audio", "UserDuckingPreference", 3);
	}

	private TweakDefinition BuildCrashAutoRestartTweak()
	{
		return BuildRegistryDwordSetTweak("windows.crash-auto-restart.disable", "Automatic Restart After System Failure Off",
			"Keeps the stop screen visible after a system failure so you can read the error. Windows may require a manual restart after a crash.",
			TweakCategory.Windows, TweakRiskLevel.Medium, new[]
			{
				new RegistryDwordTarget(Registry.LocalMachine, @"HKLM\SYSTEM\CurrentControlSet\Control\CrashControl:AutoReboot",
					@"SYSTEM\CurrentControlSet\Control\CrashControl", "AutoReboot", 0)
			}, requiresAdministrator: true, requiresRestart: true);
	}

	private TweakDefinition BuildLongPathsTweak()
	{
		return BuildRegistryDwordSetTweak("windows.long-paths.enable", "Enable Win32 Long Paths",
			"Allows compatible applications to use paths longer than 260 characters. Restart affected applications or Windows after changing this setting.",
			TweakCategory.Windows, TweakRiskLevel.Low, new[]
			{
				new RegistryDwordTarget(Registry.LocalMachine, @"HKLM\SYSTEM\CurrentControlSet\Control\FileSystem:LongPathsEnabled",
					@"SYSTEM\CurrentControlSet\Control\FileSystem", "LongPathsEnabled", 1)
			}, requiresAdministrator: true, requiresRestart: true);
	}

	private TweakDefinition BuildChromeBackgroundTweak()
	{
		return BuildRegistryDwordSetTweak("debloat.chrome-background.disable", "Chrome Background Off",
			"Disables Chrome background mode after its windows close. Background apps and extensions may stop until Chrome is opened again.",
			TweakCategory.Windows, TweakRiskLevel.Low, new[]
			{
				new RegistryDwordTarget(Registry.LocalMachine, @"HKLM\Policies\Google\Chrome:BackgroundModeEnabled",
					@"SOFTWARE\Policies\Google\Chrome", "BackgroundModeEnabled", 0)
			}, requiresAdministrator: true);
	}

	private TweakDefinition BuildChromeReportingTweak()
	{
		return BuildRegistryDwordSetTweak("debloat.chrome-reporting.disable", "Chrome Usage & Crash Reporting Off",
			"Disables Chrome's usage and crash-data reporting through its MetricsReportingEnabled policy. Close and reopen Chrome for the policy to take effect. This controls Google Chrome; other Chromium browsers have their own policies.",
			TweakCategory.Windows, TweakRiskLevel.Low, new[]
			{
				new RegistryDwordTarget(Registry.LocalMachine, @"HKLM\Policies\Google\Chrome:MetricsReportingEnabled",
					@"SOFTWARE\Policies\Google\Chrome", "MetricsReportingEnabled", 0)
			}, requiresAdministrator: true);
	}

	private TweakDefinition BuildFirefoxTelemetryTweak()
	{
		return BuildRegistryDwordSetTweak("debloat.firefox-telemetry.disable", "Firefox Telemetry Off",
			"Disables Firefox telemetry using Mozilla's DisableTelemetry enterprise policy. Restart Firefox after applying or restoring. This does not disable browser updates, Safe Browsing or Firefox crash-reporting controls.",
			TweakCategory.Windows, TweakRiskLevel.Low, new[]
			{
				new RegistryDwordTarget(Registry.LocalMachine, @"HKLM\Policies\Mozilla\Firefox:DisableTelemetry",
					@"SOFTWARE\Policies\Mozilla\Firefox", "DisableTelemetry", 1)
			}, requiresAdministrator: true);
	}

	private TweakDefinition BuildEdgeBackgroundTweak()
	{
		return BuildRegistryDwordSetTweak("debloat.edge-background.disable", "Edge Background Off",
			"Disables Edge background mode after its windows close. Background apps and extensions may stop until Edge is opened again.",
			TweakCategory.Windows, TweakRiskLevel.Low, new[]
			{
				new RegistryDwordTarget(Registry.LocalMachine, @"HKLM\Policies\Microsoft\Edge:BackgroundModeEnabled",
					@"SOFTWARE\Policies\Microsoft\Edge", "BackgroundModeEnabled", 0)
			}, requiresAdministrator: true);
	}

	private TweakDefinition BuildSettingsBackupTweak()
	{
		var tweak = BuildRegistryDwordSetTweak("windows.settings-backup.disable", "Windows Preferences Backup Off",
			"Turns off Remember my preferences in Windows backup through the Do not sync policy. Stops preferences syncing to and from this PC. OneDrive file backup and existing cloud backups are managed separately. Requires Windows Pro, Enterprise or Education.",
			TweakCategory.Windows, TweakRiskLevel.Medium, new[]
			{
				new RegistryDwordTarget(Registry.LocalMachine, @"HKLM\Policies\Windows\SettingSync:DisableSettingSync",
					@"SOFTWARE\Policies\Microsoft\Windows\SettingSync", "DisableSettingSync", 2),
				new RegistryDwordTarget(Registry.LocalMachine, @"HKLM\Policies\Windows\SettingSync:DisableSettingSyncUserOverride",
					@"SOFTWARE\Policies\Microsoft\Windows\SettingSync", "DisableSettingSyncUserOverride", 1)
			}, requiresAdministrator: true);
		return RestrictToPolicyEditions(tweak);
	}

	private TweakDefinition BuildDefenderScanCpuTweak()
	{
		var tweak = BuildRegistryDwordSetTweak("windows.defender-scan-cpu.target", "Defender Scan CPU Target · 20%",
			"Requests a 20% average CPU load target for Microsoft Defender scans using the documented scan policy. This is guidance, not a hard CPU limit, and some scan modes ignore it. Scans can take longer. Real-time protection remains enabled. Requires a supported Windows policy edition.",
			TweakCategory.Windows, TweakRiskLevel.Medium, new[]
			{
				new RegistryDwordTarget(Registry.LocalMachine, @"HKLM\Policies\WindowsDefender\Scan:AvgCPULoadFactor",
					@"SOFTWARE\Policies\Microsoft\Windows Defender\Scan", "AvgCPULoadFactor", 20)
			}, requiresAdministrator: true);
		return RestrictToPolicyEditions(tweak);
	}

	private static TweakDefinition RestrictToPolicyEditions(TweakDefinition tweak)
	{
		bool Supported()
		{
			var edition = Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion", "EditionID", "") as string ?? "";
			return Environment.OSVersion.Version.Build >= 19041 && (edition.StartsWith("Professional", StringComparison.OrdinalIgnoreCase)
				|| edition.Contains("Enterprise", StringComparison.OrdinalIgnoreCase) || edition.Contains("Education", StringComparison.OrdinalIgnoreCase));
		}
		var read = tweak.CurrentStateReader;
		var apply = tweak.ApplyAction;
		tweak.CurrentStateReader = () => Supported() ? read() : Task.FromResult(TweakState.Unsupported);
		tweak.ApplyAction = () => Supported() ? apply() : Task.FromResult(TweakActionResult.Fail("This policy requires a supported Windows edition."));
		return tweak;
	}

	private TweakDefinition BuildRecallSnapshotsTweak()
	{
		bool Supported()
		{
			const string versionKey = @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion";
			string edition = Registry.GetValue(versionKey, "EditionID", "") as string ?? "";
			int revision = Registry.GetValue(versionKey, "UBR", 0) is int ubr ? ubr : 0;
			int build = Environment.OSVersion.Version.Build;
			bool supportedEdition = edition.StartsWith("Professional", StringComparison.OrdinalIgnoreCase)
				|| edition.Contains("Enterprise", StringComparison.OrdinalIgnoreCase)
				|| edition.Contains("Education", StringComparison.OrdinalIgnoreCase);
			return supportedEdition && (build > 26100 || (build == 26100 && revision >= 3915));
		}
		var tweak = BuildRegistryDwordSetTweak("windows.recall-snapshots.disable", "Recall Snapshots Off",
			"Prevents Recall from saving snapshots. Windows deletes previously saved Recall snapshots when this policy is enabled. Restoring the policy cannot recover those snapshots.",
			TweakCategory.Windows, TweakRiskLevel.High, new[]
			{
				new RegistryDwordTarget(Registry.LocalMachine, @"HKLM\Policies\Windows\WindowsAI:DisableAIDataAnalysis",
					@"SOFTWARE\Policies\Microsoft\Windows\WindowsAI", "DisableAIDataAnalysis", 1)
			}, requiresAdministrator: true);
		var read = tweak.CurrentStateReader;
		var apply = tweak.ApplyAction;
		tweak.CurrentStateReader = () => Supported() ? read() : Task.FromResult(TweakState.Unsupported);
		tweak.ApplyAction = () => Supported() ? apply()
			: Task.FromResult(TweakActionResult.Fail("This Recall policy requires a supported Windows 11 edition and build."));
		return tweak;
	}

	private TweakDefinition BuildStorageSenseTweak()
	{
		return BuildRegistryDwordSetTweak("windows.storage-sense.disable", "Disable Storage Sense",
			"Turns off automatic Storage Sense cleanup for this PC. Use manual cleanup if disk space gets low.",
			TweakCategory.Windows, TweakRiskLevel.Medium, new[]
			{
				new RegistryDwordTarget(Registry.LocalMachine, @"HKLM\Policies\Windows\StorageSense:AllowStorageSenseGlobal",
					@"SOFTWARE\Policies\Microsoft\Windows\StorageSense", "AllowStorageSenseGlobal", 0)
			}, requiresAdministrator: true);
	}

	private TweakDefinition BuildClipboardHistoryTweak()
	{
		return BuildRegistryDwordSetTweak("windows.clipboard-history.disable", "Clipboard History Off",
			"Disables Win+V clipboard history and clipboard synchronization across devices. Standard copy and paste remains available.",
			TweakCategory.Windows, TweakRiskLevel.Medium, new[]
			{
				new RegistryDwordTarget(Registry.LocalMachine, "HKLM\\Policies\\Windows System:AllowClipboardHistory",
					@"SOFTWARE\Policies\Microsoft\Windows\System", "AllowClipboardHistory", 0),
				new RegistryDwordTarget(Registry.LocalMachine, "HKLM\\Policies\\Windows System:AllowCrossDeviceClipboard",
					@"SOFTWARE\Policies\Microsoft\Windows\System", "AllowCrossDeviceClipboard", 0)
			}, requiresAdministrator: true);
	}

	private TweakDefinition BuildWidgetsAndNewsTweak()
	{
		return BuildRegistryDwordSetTweak("windows.widgets-news.disable", "Disable Widgets, News and Interests", "Disables the Windows 11 Widgets surface and the Windows 10 News and Interests feed through their matching machine policies. This avoids feed refreshes and taskbar content.", TweakCategory.Windows, TweakRiskLevel.Low, new RegistryDwordTarget[2]
		{
			new RegistryDwordTarget(Registry.LocalMachine, "HKLM\\Policies\\Dsh:AllowNewsAndInterests", "SOFTWARE\\Policies\\Microsoft\\Dsh", "AllowNewsAndInterests", 0),
			new RegistryDwordTarget(Registry.LocalMachine, "HKLM\\Policies\\Windows Feeds:EnableFeeds", "SOFTWARE\\Policies\\Microsoft\\Windows\\Windows Feeds", "EnableFeeds", 0)
		}, requiresAdministrator: true);
	}

	private TweakDefinition BuildOneDriveFileSyncTweak()
	{
		return BuildRegistryDwordTweak("windows.onedrive-sync.disable", "Disable OneDrive File Sync", "Prevents OneDrive from being used for file storage and background synchronization. Choose this only if you do not use OneDrive; restoring removes the policy again.", TweakCategory.Windows, TweakRiskLevel.Medium, Registry.LocalMachine, "HKLM\\Policies\\OneDrive:DisableFileSyncNGSC", "SOFTWARE\\Policies\\Microsoft\\Windows\\OneDrive", "DisableFileSyncNGSC", 1, requiresAdministrator: true, requiresRestart: true);
	}

	private TweakDefinition BuildStoreAutoUpdatesTweak()
	{
		return BuildRegistryDwordTweak("windows.store-auto-updates.disable", "Disable Microsoft Store Auto-Updates", "Stops automatic download and installation of Microsoft Store app updates. You must update Store apps manually, so this optional control is not included in safe presets.", TweakCategory.Windows, TweakRiskLevel.Medium, Registry.LocalMachine, "HKLM\\Policies\\WindowsStore:AutoDownload", "SOFTWARE\\Policies\\Microsoft\\WindowsStore", "AutoDownload", 2, requiresAdministrator: true);
	}

	private TweakDefinition BuildDriverUpdatesTweak()
	{
		return BuildRegistryDwordTweak("windows.update-driver-delivery.disable", "Exclude Drivers from Windows Quality Updates", "Keeps Windows quality updates enabled while excluding driver-classified updates, so your selected GPU and chipset drivers are not replaced automatically. Drivers must then be maintained manually.", TweakCategory.Windows, TweakRiskLevel.Medium, Registry.LocalMachine, "HKLM\\Policies\\WindowsUpdate:ExcludeWUDriversInQualityUpdate", "SOFTWARE\\Policies\\Microsoft\\Windows\\WindowsUpdate", "ExcludeWUDriversInQualityUpdate", 1, requiresAdministrator: true, requiresRestart: true);
	}

	private TweakDefinition BuildCeipTweak()
	{
		return BuildRegistryDwordTweak("windows.ceip.disable", "Disable Windows CEIP", "Opts the computer out of the legacy Windows Customer Experience Improvement Program. This does not disable Windows Update, Defender, the firewall, or crash protection.", TweakCategory.Windows, TweakRiskLevel.Low, Registry.LocalMachine, "HKLM\\Policies\\SQMClient\\Windows:CEIPEnable", "SOFTWARE\\Policies\\Microsoft\\SQMClient\\Windows", "CEIPEnable", 0, requiresAdministrator: true);
	}

	private TweakDefinition BuildActivityHistoryTweak()
	{
		return BuildRegistryDwordSetTweak("windows.activity-history.disable", "Disable Windows Activity History", "Stops Windows apps and the operating system from publishing or uploading cross-device activity history. Local recent-file lists controlled by individual apps are unaffected.", TweakCategory.Windows, TweakRiskLevel.Low, new RegistryDwordTarget[3]
		{
			new RegistryDwordTarget(Registry.LocalMachine, "HKLM\\Policies\\System:EnableActivityFeed", "SOFTWARE\\Policies\\Microsoft\\Windows\\System", "EnableActivityFeed", 0),
			new RegistryDwordTarget(Registry.LocalMachine, "HKLM\\Policies\\System:PublishUserActivities", "SOFTWARE\\Policies\\Microsoft\\Windows\\System", "PublishUserActivities", 0),
			new RegistryDwordTarget(Registry.LocalMachine, "HKLM\\Policies\\System:UploadUserActivities", "SOFTWARE\\Policies\\Microsoft\\Windows\\System", "UploadUserActivities", 0)
		}, requiresAdministrator: true);
	}

	private TweakDefinition BuildWindowsErrorReportingTweak()
	{
		return BuildRegistryDwordTweak("windows.error-reporting.disable", "Disable Windows Error Reporting Uploads", "Stops Windows Error Reporting from sending application-failure reports to Microsoft. Reliability History and locally configured crash dumps remain available.", TweakCategory.Windows, TweakRiskLevel.Medium, Registry.LocalMachine, "HKLM\\Policies\\Windows Error Reporting:Disabled", "SOFTWARE\\Policies\\Microsoft\\Windows\\Windows Error Reporting", "Disabled", 1, requiresAdministrator: true);
	}

	private TweakDefinition BuildLanguageHeaderTweak()
	{
		return BuildRegistryDwordTweak("windows.language-header-tracking.disable", "Disable Language List Sharing", "Opts out of allowing websites to use the Windows language list when Windows composes the HTTP Accept-Language header. It does not remove installed languages.", TweakCategory.Windows, TweakRiskLevel.Low, Registry.CurrentUser, "HKCU\\International\\User Profile:HttpAcceptLanguageOptOut", "Control Panel\\International\\User Profile", "HttpAcceptLanguageOptOut", 1);
	}

	private TweakDefinition BuildDynamicSearchBoxTweak()
	{
		return BuildRegistryDwordTweak("windows.search-highlights.disable", "Disable Search Highlights", "Removes rotating web content and promotional highlights from the Windows taskbar search box. Normal local application and file search continues to work.", TweakCategory.Windows, TweakRiskLevel.Low, Registry.CurrentUser, "HKCU\\SearchSettings:IsDynamicSearchBoxEnabled", "Software\\Microsoft\\Windows\\CurrentVersion\\SearchSettings", "IsDynamicSearchBoxEnabled", 0);
	}

	private TweakDefinition BuildConsumerFeaturesTweak()
	{
		return BuildRegistryDwordTweak("windows.consumer-features.disable", "Disable Microsoft Consumer Features", "Disables Start suggestions, post-setup consumer app promotion, membership notifications, and redirected promotional tiles through the matching Windows policy.", TweakCategory.Windows, TweakRiskLevel.Low, Registry.LocalMachine, "HKLM\\Policies\\CloudContent:DisableWindowsConsumerFeatures", "SOFTWARE\\Policies\\Microsoft\\Windows\\CloudContent", "DisableWindowsConsumerFeatures", 1, requiresAdministrator: true);
	}

	private TweakDefinition BuildInputPersonalizationTweak()
	{
		return BuildRegistryDwordSetTweak("windows.input-personalization.disable", "Disable Ink, Typing and Contact Collection", "Disables implicit handwriting and typing collection and prevents contact harvesting used for input personalization. Keyboard, pen, and touch input continue to function.", TweakCategory.Windows, TweakRiskLevel.Low, new RegistryDwordTarget[3]
		{
			new RegistryDwordTarget(Registry.LocalMachine, "HKLM\\Policies\\InputPersonalization:RestrictImplicitInkCollection", "SOFTWARE\\Policies\\Microsoft\\InputPersonalization", "RestrictImplicitInkCollection", 1),
			new RegistryDwordTarget(Registry.LocalMachine, "HKLM\\Policies\\InputPersonalization:RestrictImplicitTextCollection", "SOFTWARE\\Policies\\Microsoft\\InputPersonalization", "RestrictImplicitTextCollection", 1),
			new RegistryDwordTarget(Registry.LocalMachine, "HKLM\\Policies\\TrainedDataStore:HarvestContacts", "SOFTWARE\\Policies\\Microsoft\\InputPersonalization\\TrainedDataStore", "HarvestContacts", 0)
		}, requiresAdministrator: true);
	}

	private TweakDefinition BuildLocationAccessTweak()
	{
		return BuildRegistryDwordTweak("windows.app-location-access.deny", "Deny Windows Apps Location Access", "Forces Windows Store apps to be denied location access. Desktop applications may use their own permission model, and maps or weather apps can lose location-aware features.", TweakCategory.Windows, TweakRiskLevel.Medium, Registry.LocalMachine, "HKLM\\Policies\\AppPrivacy:LetAppsAccessLocation", "SOFTWARE\\Policies\\Microsoft\\Windows\\AppPrivacy", "LetAppsAccessLocation", 2, requiresAdministrator: true, requiresRestart: true);
	}

	private TweakDefinition BuildDiagnosticInfoAccessTweak()
	{
		return BuildRegistryDwordTweak("windows.app-diagnostic-access.deny", "Deny Apps Diagnostic Information", "Prevents Windows Store apps from reading diagnostic information about other running apps. This is an app-privacy policy, not a change to Windows security diagnostics.", TweakCategory.Windows, TweakRiskLevel.Low, Registry.LocalMachine, "HKLM\\Policies\\AppPrivacy:LetAppsGetDiagnosticInfo", "SOFTWARE\\Policies\\Microsoft\\Windows\\AppPrivacy", "LetAppsGetDiagnosticInfo", 2, requiresAdministrator: true, requiresRestart: true);
	}

	private TweakDefinition BuildNotificationAccessTweak()
	{
		return BuildRegistryDwordTweak("windows.app-notification-access.deny", "Deny Apps Notification Access", "Prevents Windows Store apps from reading other notifications. It does not disable normal toast notifications from apps, games, Windows Security, or the system.", TweakCategory.Windows, TweakRiskLevel.Low, Registry.LocalMachine, "HKLM\\Policies\\AppPrivacy:LetAppsAccessNotifications", "SOFTWARE\\Policies\\Microsoft\\Windows\\AppPrivacy", "LetAppsAccessNotifications", 2, requiresAdministrator: true, requiresRestart: true);
	}

	private TweakDefinition BuildTelemetryTweak()
	{
		return BuildRegistryDwordSetTweak("windows.telemetry.minimum", "Set Windows Telemetry to Minimum", "Sets Windows diagnostic data to the lowest policy level accepted by the installed edition and hides the diagnostic-data opt-in page. Windows Update and security services stay enabled.", TweakCategory.Windows, TweakRiskLevel.Low, new RegistryDwordTarget[2]
		{
			new RegistryDwordTarget(Registry.LocalMachine, "HKLM\\Policies\\DataCollection:AllowTelemetry", "SOFTWARE\\Policies\\Microsoft\\Windows\\DataCollection", "AllowTelemetry", 0),
			new RegistryDwordTarget(Registry.LocalMachine, "HKLM\\Policies\\DataCollection:DisableTelemetryOptInSettingsUx", "SOFTWARE\\Policies\\Microsoft\\Windows\\DataCollection", "DisableTelemetryOptInSettingsUx", 1)
		}, requiresAdministrator: true);
	}

	private TweakDefinition BuildTypingInsightsTweak()
	{
		return BuildRegistryDwordTweak("windows.typing-insights.disable", "Disable Typing Insights", "Turns off the per-user typing-insights statistics used for personalized suggestions. Normal hardware and on-screen keyboard input continues to work.", TweakCategory.Windows, TweakRiskLevel.Low, Registry.CurrentUser, "HKCU\\Input\\Settings:InsightsEnabled", "Software\\Microsoft\\Input\\Settings", "InsightsEnabled", 0);
	}

	private TweakDefinition BuildSpeechDataCollectionTweak()
	{
		return BuildRegistryDwordTweak("windows.online-speech.disable", "Disable Online Speech Data Collection", "Opts the current user out of Windows online speech recognition uploads. Offline speech features remain available, while cloud speech and dictation may stop working.", TweakCategory.Windows, TweakRiskLevel.Medium, Registry.CurrentUser, "HKCU\\OnlineSpeechPrivacy:HasAccepted", "Software\\Microsoft\\Speech_OneCore\\Settings\\OnlineSpeechPrivacy", "HasAccepted", 0);
	}

	private TweakDefinition BuildCloudContentOptimizationTweak()
	{
		return BuildRegistryDwordTweak("windows.cloud-content-optimization.disable", "Disable Cloud-Optimized Content", "Disables cloud-optimized Start content and first-run soft-landing experiences through the matching Windows Cloud Content policy.", TweakCategory.Windows, TweakRiskLevel.Low, Registry.LocalMachine, "HKLM\\Policies\\CloudContent:DisableCloudOptimizedContent", "SOFTWARE\\Policies\\Microsoft\\Windows\\CloudContent", "DisableCloudOptimizedContent", 1, requiresAdministrator: true);
	}

	private TweakDefinition BuildOnlineTipsTweak()
	{
		return BuildRegistryDwordTweak("windows.online-tips.disable", "Disable Online Tips", "Stops the Settings application from downloading online help and tip content. Local Settings pages and Windows Update remain available.", TweakCategory.Windows, TweakRiskLevel.Low, Registry.LocalMachine, "HKLM\\Policies\\Explorer:AllowOnlineTips", "SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Policies\\Explorer", "AllowOnlineTips", 0, requiresAdministrator: true);
	}

	private TweakDefinition BuildAutomaticWindowsUpdatesTweak()
	{
		return BuildRegistryDwordTweak("windows.automatic-updates.disable", "Disable Automatic Windows Updates", "Stops Windows Update from automatically downloading and installing updates. Manual checks remain possible, but delaying security updates increases exposure and makes you responsible for staying patched.", TweakCategory.Windows, TweakRiskLevel.High, Registry.LocalMachine, "HKLM\\Policies\\WindowsUpdate\\AU:NoAutoUpdate", "SOFTWARE\\Policies\\Microsoft\\Windows\\WindowsUpdate\\AU", "NoAutoUpdate", 1, requiresAdministrator: true, requiresRestart: true);
	}

	private TweakDefinition BuildFastUserSwitchingTweak()
	{
		return BuildRegistryDwordTweak("windows.fast-user-switching.enable", "Enable Fast User Switching", "Makes the Switch user entry available without forcing the current user to sign out. This is a responsiveness and convenience policy rather than a benchmark tweak.", TweakCategory.Windows, TweakRiskLevel.Low, Registry.LocalMachine, "HKLM\\Policies\\System:HideFastUserSwitching", "SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Policies\\System", "HideFastUserSwitching", 0, requiresAdministrator: true);
	}

	private TweakDefinition BuildAutomaticMaintenanceTweak()
	{
		return BuildRegistryDwordTweak("windows.automatic-maintenance.disable", "Disable Automatic Maintenance", "Stops the Automatic Maintenance scheduler from launching its background maintenance run. Scheduled diagnostics and storage maintenance may no longer happen automatically.", TweakCategory.Windows, TweakRiskLevel.Medium, Registry.LocalMachine, "HKLM\\Maintenance:MaintenanceDisabled", "SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion\\Schedule\\Maintenance", "MaintenanceDisabled", 1, requiresAdministrator: true, requiresRestart: true);
	}

	private TweakDefinition BuildSleepStudyTweak()
	{
		return BuildRegistryDwordTweak("windows.sleep-study.disable", "Disable Sleep Study Logging", "Disables the Windows Sleep Study activity logger used on Modern Standby systems. This does not disable sleep itself, but Sleep Study reports will no longer be collected.", TweakCategory.Windows, TweakRiskLevel.Low, Registry.LocalMachine, "HKLM\\Power:SleepStudyDisabled", "SYSTEM\\CurrentControlSet\\Control\\Power", "SleepStudyDisabled", 1, requiresAdministrator: true, requiresRestart: true);
	}

	private TweakDefinition BuildAccessibilityPromptsTweak()
	{
		return BuildRegistryStringSetTweak("windows.accessibility-shortcut-prompts.disable", "Disable Accessibility Shortcut Prompts", "Stops Sticky Keys, Filter Keys, and Toggle Keys keyboard-shortcut confirmation prompts. The accessibility features remain available through Windows Settings.", TweakCategory.Windows, TweakRiskLevel.Low, new RegistryStringTarget[3]
		{
			new RegistryStringTarget(Registry.CurrentUser, "HKCU\\Accessibility\\StickyKeys:Flags", "Control Panel\\Accessibility\\StickyKeys", "Flags", "506"),
			new RegistryStringTarget(Registry.CurrentUser, "HKCU\\Accessibility\\Keyboard Response:Flags", "Control Panel\\Accessibility\\Keyboard Response", "Flags", "122"),
			new RegistryStringTarget(Registry.CurrentUser, "HKCU\\Accessibility\\ToggleKeys:Flags", "Control Panel\\Accessibility\\ToggleKeys", "Flags", "58")
		});
	}

	private TweakDefinition BuildDarkModeTweak()
	{
		return BuildRegistryDwordSetTweak("appearance.dark-mode.enable", "Enable Windows Dark Mode", "Switches both Windows system surfaces and supported applications to the dark theme. Applications with their own theme preference are unaffected.", TweakCategory.Windows, TweakRiskLevel.Low, new RegistryDwordTarget[2]
		{
			new RegistryDwordTarget(Registry.CurrentUser, "HKCU\\Themes\\Personalize:AppsUseLightTheme", "Software\\Microsoft\\Windows\\CurrentVersion\\Themes\\Personalize", "AppsUseLightTheme", 0),
			new RegistryDwordTarget(Registry.CurrentUser, "HKCU\\Themes\\Personalize:SystemUsesLightTheme", "Software\\Microsoft\\Windows\\CurrentVersion\\Themes\\Personalize", "SystemUsesLightTheme", 0)
		});
	}

	private TweakDefinition BuildNotificationCenterTweak()
	{
		return BuildRegistryDwordTweak("appearance.notification-center.disable", "Disable Notification Center", "Hides the Action Center and notification history pane. Individual toast notifications can still appear, so this does not silence Windows Security alerts.", TweakCategory.Windows, TweakRiskLevel.Medium, Registry.CurrentUser, "HKCU\\Policies\\Explorer:DisableNotificationCenter", "Software\\Policies\\Microsoft\\Windows\\Explorer", "DisableNotificationCenter", 1, requiresAdministrator: false, requiresRestart: true);
	}

	private TweakDefinition BuildClassicAltTabTweak()
	{
		return BuildRegistryDwordTweak("appearance.classic-alt-tab.enable", "Restore Classic Alt-Tab Style", "Uses the compact classic Alt-Tab switcher instead of live window thumbnails. Restore this tweak to return to the current Windows switcher.", TweakCategory.Windows, TweakRiskLevel.Low, Registry.CurrentUser, "HKCU\\Explorer:AltTabSettings", "Software\\Microsoft\\Windows\\CurrentVersion\\Explorer", "AltTabSettings", 1, requiresAdministrator: false, requiresRestart: true);
	}

	private TweakDefinition BuildTransparencyTweak()
	{
		return BuildRegistryDwordTweak("appearance.transparency.disable", "Disable Transparency Effects", "Turns off acrylic and transparency effects in supported Windows surfaces, reducing a small amount of desktop composition work without disabling the compositor.", TweakCategory.Windows, TweakRiskLevel.Low, Registry.CurrentUser, "HKCU\\Themes\\Personalize:EnableTransparency", "Software\\Microsoft\\Windows\\CurrentVersion\\Themes\\Personalize", "EnableTransparency", 0);
	}

	private TweakDefinition BuildHideNetworkInExplorerTweak()
	{
		return BuildRegistryDwordTweak("appearance.explorer-network.hide", "Hide Network in File Explorer", "Removes the Network namespace from File Explorer's navigation pane. Network shares remain reachable by path and no networking protocol is disabled.", TweakCategory.Windows, TweakRiskLevel.Low, Registry.CurrentUser, "HKCU\\Classes\\NetworkNamespace:System.IsPinnedToNameSpaceTree", "Software\\Classes\\CLSID\\{F02C1A0D-BE21-4350-88B0-7367FC96EF3C}", "System.IsPinnedToNameSpaceTree", 0, requiresAdministrator: false, requiresRestart: true);
	}

	private TweakDefinition BuildHideGalleryInExplorerTweak()
	{
		return BuildRegistryDwordTweak("appearance.explorer-gallery.hide", "Hide Gallery in File Explorer", "Removes the Windows 11 Gallery namespace from File Explorer's navigation pane without deleting or moving any pictures.", TweakCategory.Windows, TweakRiskLevel.Low, Registry.CurrentUser, "HKCU\\Classes\\GalleryNamespace:System.IsPinnedToNameSpaceTree", "Software\\Classes\\CLSID\\{e88865ea-0e1c-4e20-9aa6-edcd0212c87c}", "System.IsPinnedToNameSpaceTree", 0, requiresAdministrator: false, requiresRestart: true);
	}

	private TweakDefinition BuildHideHomeInExplorerTweak()
	{
		return BuildRegistryDwordTweak("appearance.explorer-home.hide", "Hide Home in File Explorer", "Removes the Windows 11 Home namespace from File Explorer's navigation pane. Quick Access data and files are not deleted.", TweakCategory.Windows, TweakRiskLevel.Low, Registry.CurrentUser, "HKCU\\Classes\\HomeNamespace:System.IsPinnedToNameSpaceTree", "Software\\Classes\\CLSID\\{f874310e-b6b7-47dc-bc84-b9e6b38f5903}", "System.IsPinnedToNameSpaceTree", 0, requiresAdministrator: false, requiresRestart: true);
	}

	private TweakDefinition BuildClassicContextMenuTweak()
	{
		return BuildRegistryStringSetTweak("appearance.classic-context-menu.enable", "Restore Classic Context Menu", "Restores the Windows 10-style full right-click menu on Windows 11, skipping the extra Show more options step. Explorer restart or sign-out is required.", TweakCategory.Windows, TweakRiskLevel.Low, new RegistryStringTarget[1]
		{
			new RegistryStringTarget(Registry.CurrentUser, "HKCU\\Classes\\ClassicContextMenu\\InprocServer32:(Default)", "Software\\Classes\\CLSID\\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\\InprocServer32", string.Empty, string.Empty)
		}, requiresAdministrator: false, requiresRestart: true);
	}

	private TweakDefinition BuildPreviousVersionsMenuTweak()
	{
		return BuildRegistryDwordSetTweak("appearance.previous-versions.hide", "Hide Restore Previous Versions Menu", "Hides Previous Versions pages and restore commands from Explorer. Existing restore points and shadow copies are not deleted.", TweakCategory.Windows, TweakRiskLevel.Medium, new RegistryDwordTarget[2]
		{
			new RegistryDwordTarget(Registry.CurrentUser, "HKCU\\Policies\\Explorer:NoPreviousVersionsPage", "Software\\Microsoft\\Windows\\CurrentVersion\\Policies\\Explorer", "NoPreviousVersionsPage", 1),
			new RegistryDwordTarget(Registry.CurrentUser, "HKCU\\Policies\\Explorer:NoPreviousVersionsRestore", "Software\\Microsoft\\Windows\\CurrentVersion\\Policies\\Explorer", "NoPreviousVersionsRestore", 1)
		});
	}

	private TweakDefinition BuildCpuSideChannelMitigationsTweak()
	{
		TweakDefinition tweakDefinition = new TweakDefinition();
		tweakDefinition.Id = "mitigations.cpu-side-channel.disable";
		tweakDefinition.Name = "Disable Spectre and Meltdown Mitigations";
		tweakDefinition.Description = "Disables the Windows kernel mitigations for Spectre Variant 2 and Meltdown using Microsoft's documented FeatureSettingsOverride bits. This removes important CPU isolation protections and requires a restart; it is never part of an automatic preset.";
		tweakDefinition.Category = TweakCategory.Windows;
		tweakDefinition.RiskLevel = TweakRiskLevel.High;
		tweakDefinition.RequiresAdministrator = true;
		tweakDefinition.RequiresRestart = true;
		tweakDefinition.AffectedSettings = new string[2] { "HKLM\\Memory Management:FeatureSettingsOverride[bits0-1]", "HKLM\\Memory Management:FeatureSettingsOverrideMask[bits0-1]" };
		tweakDefinition.CurrentStateReader = delegate
		{
			int valueOrDefault = RegistryHelper.ReadDword(Registry.LocalMachine, "SYSTEM\\CurrentControlSet\\Control\\Session Manager\\Memory Management", "FeatureSettingsOverride").GetValueOrDefault();
			int valueOrDefault2 = RegistryHelper.ReadDword(Registry.LocalMachine, "SYSTEM\\CurrentControlSet\\Control\\Session Manager\\Memory Management", "FeatureSettingsOverrideMask").GetValueOrDefault();
			return Task.FromResult(((valueOrDefault & 3) == 3 && (valueOrDefault2 & 3) == 3) ? TweakState.Applied : TweakState.NotApplied);
		};
		tweakDefinition.ApplyAction = delegate
		{
			int? num = RegistryHelper.ReadDword(Registry.LocalMachine, "SYSTEM\\CurrentControlSet\\Control\\Session Manager\\Memory Management", "FeatureSettingsOverride");
			int? num2 = RegistryHelper.ReadDword(Registry.LocalMachine, "SYSTEM\\CurrentControlSet\\Control\\Session Manager\\Memory Management", "FeatureSettingsOverrideMask");
			_restore.SaveBackup("mitigations.cpu-side-channel.disable", (num?.ToString() ?? "null") + "|" + (num2?.ToString() ?? "null"));
			RegistryHelper.WriteDword(Registry.LocalMachine, "SYSTEM\\CurrentControlSet\\Control\\Session Manager\\Memory Management", "FeatureSettingsOverride", num.GetValueOrDefault() | 3);
			RegistryHelper.WriteDword(Registry.LocalMachine, "SYSTEM\\CurrentControlSet\\Control\\Session Manager\\Memory Management", "FeatureSettingsOverrideMask", num2.GetValueOrDefault() | 3);
			return Task.FromResult(TweakActionResult.Ok("Spectre and Meltdown mitigations disabled. Restart Windows to apply it."));
		};
		tweakDefinition.RestoreAction = delegate
		{
			string[] array = _restore.GetBackup("mitigations.cpu-side-channel.disable")?.Split('|');
			if (array == null || array.Length != 2)
			{
				return Task.FromResult(TweakActionResult.Fail("No previous mitigation state was recorded."));
			}
			RestoreOptionalDword(Registry.LocalMachine, "SYSTEM\\CurrentControlSet\\Control\\Session Manager\\Memory Management", "FeatureSettingsOverride", array[0]);
			RestoreOptionalDword(Registry.LocalMachine, "SYSTEM\\CurrentControlSet\\Control\\Session Manager\\Memory Management", "FeatureSettingsOverrideMask", array[1]);
			_restore.ClearBackup("mitigations.cpu-side-channel.disable");
			return Task.FromResult(TweakActionResult.Ok("Spectre and Meltdown mitigation settings restored. Restart Windows to apply it."));
		};
		return tweakDefinition;
	}

	private TweakDefinition BuildHvciTweak()
	{
		return BuildRegistryDwordTweak("mitigations.hvci.disable", "Disable HVCI / Memory Integrity", "Disables Hypervisor-Protected Code Integrity. This can recover virtualization overhead but weakens kernel driver protection and can prevent Valorant, FACEIT, or other anti-cheat software from running.", TweakCategory.Windows, TweakRiskLevel.High, Registry.LocalMachine, "HKLM\\DeviceGuard\\HVCI:Enabled", "SYSTEM\\CurrentControlSet\\Control\\DeviceGuard\\Scenarios\\HypervisorEnforcedCodeIntegrity", "Enabled", 0, requiresAdministrator: true, requiresRestart: true);
	}

	private TweakDefinition BuildVbsTweak()
	{
		return BuildRegistryDwordTweak("mitigations.vbs.disable", "Disable Virtualization-Based Security", "Disables the Windows VBS policy used by Credential Guard and Memory Integrity. This weakens credential and kernel isolation and may conflict with anti-cheat or managed-device policy.", TweakCategory.Windows, TweakRiskLevel.High, Registry.LocalMachine, "HKLM\\DeviceGuard:EnableVirtualizationBasedSecurity", "SYSTEM\\CurrentControlSet\\Control\\DeviceGuard", "EnableVirtualizationBasedSecurity", 0, requiresAdministrator: true, requiresRestart: true);
	}

	private TweakDefinition BuildHypervisorLaunchTweak()
	{
		TweakDefinition tweakDefinition = new TweakDefinition();
		tweakDefinition.Id = "mitigations.hypervisor-launch.disable";
		tweakDefinition.Name = "Disable Hypervisor Launch";
		tweakDefinition.Description = "Sets the Windows hypervisor launch type to Off. Hyper-V, Windows Sandbox, WSL2, VBS, and virtualization-backed anti-cheat requirements may stop working until this is restored.";
		tweakDefinition.Category = TweakCategory.Windows;
		tweakDefinition.RiskLevel = TweakRiskLevel.High;
		tweakDefinition.RequiresAdministrator = true;
		tweakDefinition.RequiresRestart = true;
		tweakDefinition.AffectedSettings = new string[1] { "bcd:current:hypervisorlaunchtype" };
		tweakDefinition.CurrentStateReader = async () => ((await GetBcdCurrentValueAsync("hypervisorlaunchtype"))?.Equals("Off", StringComparison.OrdinalIgnoreCase) ?? false) ? TweakState.Applied : TweakState.NotApplied;
		tweakDefinition.ApplyAction = async delegate
		{
			string text = await GetBcdCurrentValueAsync("hypervisorlaunchtype");
			_restore.SaveBackup("mitigations.hypervisor-launch.disable", text ?? "null");
			return ((await RunProcessAsync("bcdedit", "/set {current} hypervisorlaunchtype off")).Item1 == 0) ? TweakActionResult.Ok("Hypervisor launch disabled. Restart Windows to apply it.") : TweakActionResult.Fail("Windows boot configuration rejected the hypervisor change.");
		};
		tweakDefinition.RestoreAction = async delegate
		{
			string backup = _restore.GetBackup("mitigations.hypervisor-launch.disable");
			if (backup == null)
			{
				return TweakActionResult.Fail("No previous hypervisor launch state was recorded.");
			}
			string arguments = ((backup == "null") ? "/deletevalue {current} hypervisorlaunchtype" : ("/set {current} hypervisorlaunchtype " + backup));
			(int, string) obj = await RunProcessAsync("bcdedit", arguments);
			if (obj.Item1 == 0)
			{
				_restore.ClearBackup("mitigations.hypervisor-launch.disable");
			}
			return (obj.Item1 == 0) ? TweakActionResult.Ok("Hypervisor launch setting restored. Restart Windows to apply it.") : TweakActionResult.Fail("Windows could not restore the hypervisor launch setting.");
		};
		return tweakDefinition;
	}

	private TweakDefinition BuildSmartScreenTweak()
	{
		return BuildRegistryDwordTweak("mitigations.smartscreen.disable", "Disable Microsoft Defender SmartScreen", "Disables the Windows shell reputation check for downloaded applications. Unknown or malicious downloads will lose an important warning layer; Defender real-time scanning is not disabled.", TweakCategory.Windows, TweakRiskLevel.High, Registry.LocalMachine, "HKLM\\Policies\\System:EnableSmartScreen", "SOFTWARE\\Policies\\Microsoft\\Windows\\System", "EnableSmartScreen", 0, requiresAdministrator: true, requiresRestart: true);
	}

	private TweakDefinition BuildMarkOfTheWebTweak()
	{
		return BuildRegistryDwordTweak("mitigations.mark-of-the-web.disable", "Disable Mark of the Web", "Stops Windows Attachment Manager from preserving Internet-zone information on downloaded files. Office Protected View and other downloaded-file warnings may no longer appear.", TweakCategory.Windows, TweakRiskLevel.High, Registry.CurrentUser, "HKCU\\Policies\\Attachments:SaveZoneInformation", "Software\\Microsoft\\Windows\\CurrentVersion\\Policies\\Attachments", "SaveZoneInformation", 1, requiresAdministrator: false, requiresRestart: true);
	}

	private TweakDefinition BuildSystemRestoreServicesTweak()
	{
		return BuildServiceGroupTweak("services.system-restore.disable", "Disable System Restore Services", "Disables Volume Shadow Copy and its software provider. This stops normal restore-point and shadow-copy creation and can affect backup software; existing restore data is not deleted.", TweakRiskLevel.High, new string[2] { "VSS", "swprv" });
	}

	private TweakDefinition BuildVpnServicesTweak()
	{
		return BuildServiceGroupTweak("services.vpn-stack.disable", "Disable Windows VPN Stack", "Disables Windows dial-up, SSTP, IKE/IPsec, Remote Access, and IP Helper services. Built-in VPN connections and some third-party VPN or IPv6 tunnel features may stop working.", TweakRiskLevel.High, new string[6] { "RasAuto", "RasMan", "SstpSvc", "IKEEXT", "PolicyAgent", "iphlpsvc" });
	}

	private TweakDefinition BuildWifiServicesTweak()
	{
		return BuildServiceGroupTweak("services.wifi-stack.disable", "Disable Wi-Fi Stack", "Disables WLAN AutoConfig. Only use this on a wired desktop that will not need Wi-Fi; applying it while connected wirelessly will disconnect the PC.", TweakRiskLevel.High, new string[1] { "WlanSvc" });
	}

	private TweakDefinition BuildBluetoothServicesTweak()
	{
		return BuildServiceGroupTweak("services.bluetooth-stack.disable", "Disable Bluetooth Services", "Disables the main Windows Bluetooth support, audio gateway, and AVCTP services. Bluetooth headsets, controllers, keyboards, and pairing will stop working.", TweakRiskLevel.Medium, new string[3] { "bthserv", "BTAGService", "BthAvctpSvc" });
	}

	private TweakDefinition BuildPrintingServicesTweak()
	{
		return BuildServiceGroupTweak("services.printing-scanning.disable", "Disable Printing and Scanning Services", "Disables Print Spooler, printer notifications, and Windows Image Acquisition. Printing, scanning, and applications that depend on these services will stop working.", TweakRiskLevel.Medium, new string[3] { "Spooler", "PrintNotify", "stisvc" });
	}

	private TweakDefinition BuildDiagnosticsServicesTweak()
	{
		return BuildServiceGroupTweak("services.diagnostics-telemetry.disable", "Disable Diagnostics and Telemetry Services", "Disables connected telemetry, Windows Error Reporting, diagnostics policy, and diagnostic host services. Some troubleshooters, automated repair, and crash reporting will stop working.", TweakRiskLevel.High, new string[6] { "DiagTrack", "WerSvc", "diagnosticshub.standardcollector.service", "DPS", "WdiServiceHost", "WdiSystemHost" });
	}

	private TweakDefinition BuildServiceGroupTweak(string id, string name, string description, TweakRiskLevel risk, IReadOnlyList<string> serviceNames)
	{
		return new TweakDefinition
		{
			Id = id,
			Name = name,
			Description = description,
			Category = TweakCategory.Windows,
			RiskLevel = risk,
			RequiresAdministrator = true,
			RequiresRestart = false,
			AffectedSettings = serviceNames.Select((string serviceName) => "service:" + serviceName + ":start-type").ToArray(),
			CurrentStateReader = async delegate
			{
				List<int> presentStates = new List<int>();
				foreach (string serviceName2 in serviceNames)
				{
					int? num = await GetServiceStartTypeAsync(serviceName2);
					if (num.HasValue)
					{
						presentStates.Add(num.Value);
					}
				}
				return (presentStates.Count == 0) ? TweakState.Unsupported : (presentStates.All((int startType) => startType == 4) ? TweakState.Applied : TweakState.NotApplied);
			},
			ApplyAction = async delegate
			{
				Dictionary<string, ServiceBackupState> backups = new Dictionary<string, ServiceBackupState>(StringComparer.OrdinalIgnoreCase);
				foreach (string serviceName in serviceNames)
				{
					int? startType = await GetServiceStartTypeAsync(serviceName);
					if (startType.HasValue)
					{
						bool valueOrDefault = await GetServiceRunningStateAsync(serviceName) == true;
						int? delayed = RegistryHelper.ReadDword(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Services\" + serviceName, "DelayedAutoStart");
						backups[serviceName] = new ServiceBackupState(startType.Value, valueOrDefault, delayed, true);
					}
				}
				if (backups.Count == 0)
				{
					return TweakActionResult.Fail("None of the services in this group exist on this Windows installation.");
				}
				string existingBackup = _restore.GetBackup(id);
				if (existingBackup != null)
				{
					Dictionary<string, ServiceBackupState> originals;
					try { originals = JsonSerializer.Deserialize<Dictionary<string, ServiceBackupState>>(existingBackup); }
					catch (JsonException) { return TweakActionResult.Fail("The existing service backup is invalid. No services were changed."); }
					if (originals == null || originals.Count == 0 || originals.Any(item => !serviceNames.Contains(item.Key, StringComparer.OrdinalIgnoreCase)
						|| item.Value == null || item.Value.StartType < 2 || item.Value.StartType > 4))
						return TweakActionResult.Fail("The existing service backup is invalid. No services were changed.");
					if (backups.Any(item => !originals.TryGetValue(item.Key, out var original)
						|| (item.Value.StartType != 4 && item.Value.StartType != original.StartType)))
						return TweakActionResult.Fail("The service group changed since its original was saved. Restore it before applying again. No services were changed.");
				}
				_restore.SaveBackup(id, JsonSerializer.Serialize(backups));
				bool allSucceeded = true;
				foreach (string serviceName in backups.Keys)
				{
					await RunProcessAsync("sc", "stop " + QuoteServiceName(serviceName));
					if ((await RunProcessAsync("sc", "config " + QuoteServiceName(serviceName) + " start= disabled")).Item1 != 0)
					{
						allSucceeded = false;
					}
					if (await GetServiceStartTypeAsync(serviceName) != 4 || await GetServiceRunningStateAsync(serviceName) != false)
						allSucceeded = false;
				}
				return allSucceeded ? TweakActionResult.Ok(name + " applied.") : TweakActionResult.Fail("One or more services rejected the startup change; recorded services can still be restored.");
			},
			RestoreAction = async delegate
			{
				string backup = _restore.GetBackup(id);
				if (backup == null)
				{
					return TweakActionResult.Fail("No previous service configuration was recorded.");
				}
				Dictionary<string, ServiceBackupState> dictionary;
				try
				{
					dictionary = JsonSerializer.Deserialize<Dictionary<string, ServiceBackupState>>(backup);
				}
				catch (JsonException)
				{
					return TweakActionResult.Fail("The recorded service configuration is invalid.");
				}
				if (dictionary == null || dictionary.Count == 0
                    || dictionary.Any(item => !serviceNames.Contains(item.Key, StringComparer.OrdinalIgnoreCase)
                        || item.Value == null || item.Value.StartType < 2 || item.Value.StartType > 4
                        || (item.Value.HasDelayBackup && item.Value.DelayedAutoStart is not null and not 0 and not 1)))
				{
					return TweakActionResult.Fail("The recorded service configuration is empty.");
				}
				bool allSucceeded = true;
				foreach (KeyValuePair<string, ServiceBackupState> item in dictionary)
				{
					item.Deconstruct(out var key, out var value);
					string serviceName = key;
					ServiceBackupState backup2 = value;
					int? currentStart = await GetServiceStartTypeAsync(serviceName);
					if (!currentStart.HasValue || (currentStart != 4 && currentStart != backup2.StartType))
					{
						allSucceeded = false;
						continue;
					}
					string text = MapStartTypeToScKeyword(backup2.StartType);
					if ((await RunProcessAsync("sc", "config " + QuoteServiceName(serviceName) + " start= " + text)).Item1 != 0)
					{
						allSucceeded = false;
					}
					else if (backup2.WasRunning)
					{
						await RunProcessAsync("sc", "start " + QuoteServiceName(serviceName));
						if (await GetServiceRunningStateAsync(serviceName) != true)
							allSucceeded = false;
					}
					if (await GetServiceStartTypeAsync(serviceName) != backup2.StartType)
						allSucceeded = false;
					if (backup2.HasDelayBackup)
					{
						string servicePath = @"SYSTEM\CurrentControlSet\Services\" + serviceName;
						if (backup2.DelayedAutoStart.HasValue)
							RegistryHelper.WriteDword(Registry.LocalMachine, servicePath, "DelayedAutoStart", backup2.DelayedAutoStart.Value);
						else
							RegistryHelper.DeleteValue(Registry.LocalMachine, servicePath, "DelayedAutoStart");
						if (RegistryHelper.ReadDword(Registry.LocalMachine, servicePath, "DelayedAutoStart") != backup2.DelayedAutoStart)
							allSucceeded = false;
					}
				}
				if (allSucceeded)
				{
					_restore.ClearBackup(id);
				}
				return allSucceeded ? TweakActionResult.Ok(name + " restored.") : TweakActionResult.Fail("One or more services could not be restored.");
			}
		};
	}

	private TweakDefinition BuildMenuDelayTweak()
	{
		TweakDefinition tweakDefinition = new TweakDefinition();
		tweakDefinition.Id = "windows.menu-delay.reduce";
		tweakDefinition.Name = "Remove Menu Show Delay";
		tweakDefinition.Description = "Sets the standard Windows submenu hover delay to zero using the supported Windows system-parameter API. This affects UI responsiveness only and is fully reversible.";
		tweakDefinition.Category = TweakCategory.Windows;
		tweakDefinition.RiskLevel = TweakRiskLevel.Low;
		tweakDefinition.RequiresAdministrator = false;
		tweakDefinition.RequiresRestart = false;
		tweakDefinition.AffectedSettings = new string[1] { "user32:menu-show-delay" };
		tweakDefinition.CurrentStateReader = () => Task.FromResult((!TryGetSystemParameter(106u, out var value)) ? TweakState.Unsupported : ((value == 0) ? TweakState.Applied : TweakState.NotApplied));
		tweakDefinition.ApplyAction = delegate
		{
			if (!TryGetSystemParameter(106u, out var value))
			{
				return Task.FromResult(TweakActionResult.Fail("Windows menu timing is unavailable."));
			}
			_restore.SaveBackup("windows.menu-delay.reduce", value.ToString());
			return Task.FromResult(SetSystemParameter(107u, 0u) ? TweakActionResult.Ok("Menu show delay removed.") : TweakActionResult.Fail("Windows did not accept the menu timing change."));
		};
		tweakDefinition.RestoreAction = delegate
		{
			if (!uint.TryParse(_restore.GetBackup("windows.menu-delay.reduce"), out var result))
			{
				return Task.FromResult(TweakActionResult.Fail("No previous menu timing was recorded."));
			}
			bool num = SetSystemParameter(107u, result);
			if (num)
			{
				_restore.ClearBackup("windows.menu-delay.reduce");
			}
			return Task.FromResult(num ? TweakActionResult.Ok("Menu show delay restored.") : TweakActionResult.Fail("Windows did not restore the menu timing."));
		};
		return tweakDefinition;
	}

	private TweakDefinition BuildRegistryStringSetTweak(string id, string name, string description, TweakCategory category, TweakRiskLevel risk, IReadOnlyList<RegistryStringTarget> settings, bool requiresAdministrator = false, bool requiresRestart = false)
		=> RegistryStringTweaks.Build(id, name, description, category, risk,
			settings.Select(item => new RegistryStringSetting(item.Hive, item.AffectedSetting, item.Path, item.ValueName, item.OptimizedValue)).ToArray(), _restore, requiresAdministrator, requiresRestart);
	private TweakDefinition BuildRegistryDwordSetTweak(string id, string name, string description, TweakCategory category, TweakRiskLevel risk, IReadOnlyList<RegistryDwordTarget> settings, bool requiresAdministrator = false, bool requiresRestart = false)
	{
		return new TweakDefinition
		{
			Id = id,
			Name = name,
			Description = description,
			Category = category,
			RiskLevel = risk,
			RequiresAdministrator = requiresAdministrator,
			RequiresRestart = requiresRestart,
			AffectedSettings = settings.Select((RegistryDwordTarget setting) => setting.AffectedSetting).ToArray(),
			CurrentStateReader = () => Task.FromResult(settings.All((RegistryDwordTarget setting) => RegistryHelper.ReadDword(setting.Hive, setting.Path, setting.ValueName) == setting.OptimizedValue) ? TweakState.Applied : TweakState.NotApplied),
			ApplyAction = delegate
			{
				IEnumerable<string> values = settings.Select((RegistryDwordTarget setting) => RegistryHelper.ReadDword(setting.Hive, setting.Path, setting.ValueName)?.ToString() ?? "null");
				if (_restore.GetBackup(id) == null)
					_restore.SaveBackup(id, string.Join('|', values));
				foreach (RegistryDwordTarget setting in settings)
				{
					RegistryHelper.WriteDword(setting.Hive, setting.Path, setting.ValueName, setting.OptimizedValue);
				}
				if (settings.Any(setting => RegistryHelper.ReadDword(setting.Hive, setting.Path, setting.ValueName) != setting.OptimizedValue))
					return Task.FromResult(TweakActionResult.Fail("Windows did not retain every requested value. Restore data was kept."));
				return Task.FromResult(TweakActionResult.Ok(name + " applied."));
			},
			RestoreAction = delegate
			{
				string backup = _restore.GetBackup(id);
				if (backup == null)
				{
					return Task.FromResult(TweakActionResult.Fail("No previous values were recorded."));
				}
				string[] array = backup.Split('|');
				if (array.Length != settings.Count || array.Any((string part) => part != "null" && !int.TryParse(part, out var _)))
				{
					return Task.FromResult(TweakActionResult.Fail("The recorded restore values are invalid."));
				}
				for (int num = 0; num < settings.Count; num++)
				{
					RegistryDwordTarget registryDwordTarget = settings[num];
					RestoreOptionalDword(registryDwordTarget.Hive, registryDwordTarget.Path, registryDwordTarget.ValueName, array[num]);
				}
				for (int index = 0; index < settings.Count; index++)
				{
					var setting = settings[index];
					string actual = RegistryHelper.ReadDword(setting.Hive, setting.Path, setting.ValueName)?.ToString() ?? "null";
					if (actual != array[index])
						return Task.FromResult(TweakActionResult.Fail("Windows did not retain every restored value. Restore data was kept."));
				}
				_restore.ClearBackup(id);
				return Task.FromResult(TweakActionResult.Ok(name + " restored."));
			}
		};
	}

	private TweakDefinition BuildRegistryDwordTweak(string id, string name, string description, TweakCategory category, TweakRiskLevel risk, RegistryKey hive, string affectedSetting, string path, string valueName, int optimizedValue, bool requiresAdministrator = false, bool requiresRestart = false)
		=> RegistryDwordTweaks.Build(id,name,description,category,risk,hive,affectedSetting,path,valueName,optimizedValue,_restore,requiresAdministrator,requiresRestart);
	private static void RestoreOptionalDword(RegistryKey hive, string path, string valueName, string serializedValue)
	{
		int result;
		if (serializedValue == "null")
		{
			RegistryHelper.DeleteValue(hive, path, valueName);
		}
		else if (int.TryParse(serializedValue, out result))
		{
			RegistryHelper.WriteDword(hive, path, valueName, result);
		}
	}

	private static async Task<string?> RunPowercfgAsync(string arguments)
	{
		_ = 2;
		try
		{
			ProcessStartInfo startInfo = new ProcessStartInfo
			{
				FileName = "powercfg",
				Arguments = arguments,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				UseShellExecute = false,
				CreateNoWindow = true
			};
			using Process process = Process.Start(startInfo);
			if (process == null)
			{
				return null;
			}
			Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
			Task<string> errorTask = process.StandardError.ReadToEndAsync();
			await process.WaitForExitAsync();
			await errorTask;
			return (process.ExitCode == 0) ? (await outputTask) : null;
		}
		catch
		{
			return null;
		}
	}

	private static string? ExtractSchemeGuid(string? getActiveSchemeOutput)
	{
		if (string.IsNullOrWhiteSpace(getActiveSchemeOutput))
		{
			return null;
		}
		string[] array = getActiveSchemeOutput.Split(':');
		if (array.Length < 2)
		{
			return null;
		}
		return array[1].Trim().Split(' ').FirstOrDefault();
	}

	private static string? ExtractSchemeGuidByName(string? listOutput, string schemeName)
	{
		if (string.IsNullOrWhiteSpace(listOutput))
		{
			return null;
		}
		string[] array = listOutput.Split('\n');
		foreach (string text in array)
		{
			if (text.Contains("(" + schemeName + ")", StringComparison.OrdinalIgnoreCase))
			{
				Match match = Regex.Match(text, "[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}");
				if (match.Success)
				{
					return match.Value;
				}
			}
		}
		return null;
	}

	private static NetworkInterface? GetActiveAdapter()
	{
		return (from n in NetworkInterface.GetAllNetworkInterfaces()
			where n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback && n.NetworkInterfaceType != NetworkInterfaceType.Tunnel
			orderby n.Speed descending
			select n).FirstOrDefault();
	}

	private static async Task<(int ExitCode, string StdOut)> RunProcessAsync(string fileName, string arguments)
	{
		_ = 2;
		try
		{
			ProcessStartInfo startInfo = new ProcessStartInfo
			{
				FileName = fileName,
				Arguments = arguments,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				UseShellExecute = false,
				CreateNoWindow = true
			};
			using Process process = Process.Start(startInfo);
			if (process == null)
			{
				return (ExitCode: -1, StdOut: string.Empty);
			}
			Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
			Task<string> errorTask = process.StandardError.ReadToEndAsync();
			await process.WaitForExitAsync();
			await errorTask;
			int exitCode = process.ExitCode;
			int item = exitCode;
			return (ExitCode: item, StdOut: await outputTask);
		}
		catch
		{
			return (ExitCode: -1, StdOut: string.Empty);
		}
	}

	private static Task<(int ExitCode, string StdOut)> RunPowerShellAsync(string command)
	{
		return RunProcessAsync("powershell.exe", "-NoLogo -NoProfile -NonInteractive -Command \"" + command + "\"");
	}

	private static async Task<bool?> GetMemoryCompressionStateAsync()
	{
		(int, string) tuple = await RunPowerShellAsync("(Get-MMAgent).MemoryCompression");
		if (tuple.Item1 != 0)
		{
			return null;
		}
		bool result;
		return bool.TryParse(tuple.Item2.Trim(), out result) ? new bool?(result) : ((bool?)null);
	}

	private static async Task<bool?> GetPageCombiningStateAsync()
	{
		(int, string) tuple = await RunPowerShellAsync("(Get-MMAgent).PageCombining");
		if (tuple.Item1 != 0)
		{
			return null;
		}
		bool result;
		return bool.TryParse(tuple.Item2.Trim(), out result) ? new bool?(result) : ((bool?)null);
	}

	private static bool? GetHibernationEnabled()
	{
		return HibernationState.ReadEnabled();
	}

	private static async Task<string?> GetBcdCurrentValueAsync(string elementName)
	{
		(int, string) tuple = await RunProcessAsync("bcdedit", "/enum {current}");
		if (tuple.Item1 != 0)
		{
			return null;
		}
		Match match = Regex.Match(tuple.Item2, "^\\s*" + Regex.Escape(elementName) + "\\s+(\\S+)\\s*$", RegexOptions.IgnoreCase | RegexOptions.Multiline);
		return match.Success ? match.Groups[1].Value : null;
	}

	private static async Task<bool?> GetTcpHeuristicsStateAsync()
	{
		(int, string) tuple = await RunProcessAsync("netsh", "int tcp show heuristics");
		if (tuple.Item1 != 0)
		{
			return null;
		}
		Match match = Regex.Match(tuple.Item2, "heuristics\\s*:\\s*(enabled|disabled)", RegexOptions.IgnoreCase);
		if (!match.Success)
		{
			return null;
		}
		return match.Groups[1].Value.Equals("enabled", StringComparison.OrdinalIgnoreCase);
	}

	private static List<NetBiosAdapterState> GetNetBiosAdapterStates()
	{
		List<NetBiosAdapterState> list = new List<NetBiosAdapterState>();
		try
		{
			using ManagementObjectSearcher managementObjectSearcher = new ManagementObjectSearcher("SELECT SettingID, TcpipNetbiosOptions FROM Win32_NetworkAdapterConfiguration WHERE IPEnabled = TRUE");
			using ManagementObjectCollection managementObjectCollection = managementObjectSearcher.Get();
			foreach (ManagementObject item in managementObjectCollection)
			{
				using (item)
				{
					string text = item["SettingID"]?.ToString();
					if (!string.IsNullOrWhiteSpace(text))
					{
						uint mode = Convert.ToUInt32(item["TcpipNetbiosOptions"] ?? ((object)0u));
						list.Add(new NetBiosAdapterState(text, mode));
					}
				}
			}
			return list;
		}
		catch
		{
			return new List<NetBiosAdapterState>();
		}
	}

	private static bool SetNetBiosModes(IReadOnlyDictionary<string, uint> modes)
	{
		int num = 0;
		bool flag = true;
		try
		{
			using ManagementObjectSearcher managementObjectSearcher = new ManagementObjectSearcher("SELECT SettingID, TcpipNetbiosOptions FROM Win32_NetworkAdapterConfiguration WHERE IPEnabled = TRUE");
			using ManagementObjectCollection managementObjectCollection = managementObjectSearcher.Get();
			foreach (ManagementObject item in managementObjectCollection)
			{
				using (item)
				{
					string text = item["SettingID"]?.ToString();
					if (text != null && modes.TryGetValue(text, out var value))
					{
						num++;
						uint num2 = Convert.ToUInt32(item.InvokeMethod("SetTcpipNetbios", new object[1] { value }) ?? ((object)uint.MaxValue));
						if (num2 != 0 && num2 != 1)
						{
							flag = false;
						}
					}
				}
			}
		}
		catch
		{
			return false;
		}
		return num > 0 && flag;
	}

	private static async Task<int?> GetTrimDisabledStateAsync()
	{
		(int, string) tuple = await RunProcessAsync("fsutil", "behavior query DisableDeleteNotify");
		if (tuple.Item1 != 0)
		{
			return null;
		}
		Match match = Regex.Match(tuple.Item2, "NTFS\\s+DisableDeleteNotify\\s*=\\s*([01])", RegexOptions.IgnoreCase);
		int result;
		return (match.Success && int.TryParse(match.Groups[1].Value, out result)) ? new int?(result) : ((int?)null);
	}

	[DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool SystemParametersInfoGet(uint uiAction, uint uiParam, out uint pvParam, uint fWinIni);

	[DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool SystemParametersInfoSet(uint uiAction, uint uiParam, nint pvParam, uint fWinIni);

	private static bool TryGetSystemParameter(uint action, out uint value)
	{
		return SystemParametersInfoGet(action, 0u, out value, 0u);
	}

	private static bool SetSystemParameter(uint action, uint value)
	{
		return SystemParametersInfoSet(action, value, IntPtr.Zero, 3u);
	}

	private static async Task<int?> GetServiceStartTypeAsync(string serviceName)
	{
		int? serviceType = RegistryHelper.ReadDword(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Services\" + serviceName, "Type");
		if (!serviceType.HasValue || !IsUserModeService(serviceType.Value)) return null;
		var (num, input) = await RunProcessAsync("sc", "qc " + QuoteServiceName(serviceName));
		if (num != 0)
		{
			return null;
		}
		Match match = Regex.Match(input, "START_TYPE\\s*:\\s*(\\d+)");
		int result;
		return (match.Success && int.TryParse(match.Groups[1].Value, out result)) ? new int?(result) : ((int?)null);
	}

	private static async Task<bool?> GetServiceRunningStateAsync(string serviceName)
	{
		var (num, input) = await RunProcessAsync("sc", "query " + QuoteServiceName(serviceName));
		if (num != 0)
		{
			return null;
		}
		Match match = Regex.Match(input, "STATE\\s*:\\s*(\\d+)");
		int result;
		return (match.Success && int.TryParse(match.Groups[1].Value, out result)) ? new bool?(result == 4) : ((bool?)null);
	}

	private static bool IsUserModeService(int type) => (type & 0x30) != 0 && (type & 0x0F) == 0;

	private static string QuoteServiceName(string name)
	{
		if (string.IsNullOrWhiteSpace(name) || name.IndexOfAny(new[] { '"', '\\', '/', '\0', '\r', '\n' }) >= 0)
			throw new ArgumentException("Invalid Windows service name.", nameof(name));
		return "\"" + name + "\"";
	}

	private static string MapStartTypeToScKeyword(int code)
	{
		return code switch
		{
			2 => "auto", 
			3 => "demand", 
			4 => "disabled", 
			_ => "demand", 
		};
	}
}















