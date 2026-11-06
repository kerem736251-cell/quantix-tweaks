using System;
using System.Collections.Generic;
using System.Linq;
using QuantixTweakingUtility.Models;
using QuantixTweakingUtility.Services;

namespace QuantixTweakingUtility.ViewModels;

public static class TweakTabCatalog
{
	private static readonly (string Key, string Title, string Description)[] TabDefinitions = new(string, string, string)[]
	{
		("basic", "Basic", "Fine-tune Windows for responsiveness and headroom."),
		("processes", "Process Lowering", "Review startup activity, optional background services, and cleanup tools."),
		("debloat", "Debloat Optimization", "Trim background browser activity and review cleanup tools."),
		("privacy", "Privacy", "Telemetry, advertising, input personalization, content, search, and app-permission controls."),
		("power", "Power", "CPU, hibernation, timer, and device power behavior. Desktop-oriented settings are clearly marked."),
		("gpu", "GPU", "Quantix graphics controls: Windows scheduling, NVIDIA profiles, and AMD Radeon driver settings."),
		("precision", "Precision", "Mouse consistency and pointer controls. These change Windows pointer behavior; games using raw input handle aiming separately. Keep your in-game sensitivity consistent."),
		("network", "Network", "TCP, DNS, adapter, delivery, and legacy protocol controls for a cleaner network stack."),
		("advanced", "Extra Optimization", "Game priority, keyboard, mouse, memory, and additional controls."),
		("mitigations", "Mitigations", "Optional high-risk security and virtualization controls. Nothing here is applied automatically."),
		("services", "Services", "Reversible service-stack groups for hardware or Windows features you do not use."),
		("appearance", "Appearance", "Windows visual behavior, File Explorer namespaces, and context-menu preferences.")
	};

	public static IReadOnlyDictionary<string, TweakTabViewModel> Build(ITweakService tweakService)
	{
		Dictionary<string, TweakTabViewModel> dictionary = TabDefinitions.ToDictionary<(string, string, string), string, TweakTabViewModel>(((string Key, string Title, string Description) definition) => definition.Key, delegate((string Key, string Title, string Description) definition)
		{
			TweakTabViewModel tweakTabViewModel = new TweakTabViewModel();
			(tweakTabViewModel.Key, tweakTabViewModel.Title, tweakTabViewModel.Description) = definition;
			return tweakTabViewModel;
		}, StringComparer.OrdinalIgnoreCase);
		Dictionary<string, Dictionary<string, TweakGroupViewModel>> dictionary2 = dictionary.Keys.ToDictionary<string, string, Dictionary<string, TweakGroupViewModel>>((string key) => key, (string _) => new Dictionary<string, TweakGroupViewModel>(StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);
		foreach (TweakDefinition allTweak in tweakService.GetAllTweaks())
		{
			if(allTweak.Category==TweakCategory.Gpu && allTweak.RestoreOnly)continue;
			(string, string) tuple = ResolvePlacement(allTweak);
			Dictionary<string, TweakGroupViewModel> dictionary3 = dictionary2[tuple.Item1];
			if (!dictionary3.TryGetValue(tuple.Item2, out var value))
			{
				value = new TweakGroupViewModel
				{
					Name = tuple.Item2
				};
				dictionary3[tuple.Item2] = value;
				if (tuple.Item2 == "POWER TWEAKS")
					dictionary[tuple.Item1].Groups.Insert(0, value);
				else
					dictionary[tuple.Item1].Groups.Add(value);
			}
			value.Rows.Add(new TweakRowViewModel(allTweak, tweakService));
		}
		List<string> list = (from row in dictionary.Values.SelectMany((TweakTabViewModel tab) => tab.Groups).SelectMany((TweakGroupViewModel @group) => @group.Rows)
			select row.Definition.Id).ToList();
		if (list.Count != tweakService.GetAllTweaks().Count(t=>t.Category!=TweakCategory.Gpu || !t.RestoreOnly) || list.Distinct<string>(StringComparer.OrdinalIgnoreCase).Count() != list.Count)
		{
			throw new InvalidOperationException("The tweak tab catalog must place every tweak exactly once.");
		}
		return dictionary;
	}

	private static (string Tab, string Group) ResolvePlacement(TweakDefinition tweak)
	{
		string id = tweak.Id;
		if(id==WindowsTelemetryTasksTweak.Id)return ("gpu","NVIDIA DRIVER");
		if(id=="game.discord-hardware-acceleration.disable")return ("advanced","GAME TWEAKS");
		if(id.StartsWith("boot.",StringComparison.Ordinal))return ("basic","BCD-EDIT");
		if(id=="input.usb-controller-msi.enable")return ("basic","USB TWEAKS");
		if(id=="storage.nvme-msi.enable")return ("basic","STORAGE TWEAKS");
		if(id is "input.keyboard-queue.200" or "input.mouse-queue.200")return ("advanced","KBM TWEAKS");
		if (id is "windows.fast-shutdown" or "windows.resource-policy" or "windows.timer-resolution.1ms" or "windows.mmcss.audio-window") return ("basic","PERFORMANCE TWEAKS");
		if (id is "windows.explorer.single-process" or "windows.audio-ducking.disable" or "appearance.classic-alt-tab.enable" or "windows.uac.disable") return ("basic","EXTRA TWEAKS");
		if (id is "storage.trim.enable" or "storage.optimize-system-drive" or "windows.gpu-tdr.disable") return ("basic","STORAGE TWEAKS");
		if (id.StartsWith("windows.cleanup.",StringComparison.Ordinal)) return ("basic","CLEANER");
		if (id == "power.dynamic-tick.disable") return ("basic", "BCD-EDIT");
		if (id.StartsWith("power.network.", StringComparison.Ordinal)) return ("power", "POWER TWEAKS");
		if (id is "windows.fault-tolerant-heap.disable" or "windows.insider-builds.disable"
			or "gaming.mmcss-priority.boost" or "mitigations.cpu-side-channel.disable" or "mitigations.hvci.disable" or "mitigations.vbs.disable"
			or "perf.power-throttling.disable" or "windows.onedrive-sync.disable" or "advanced.search-indexing-service.disable"
			or "windows.error-reporting.disable" or "advanced.sysmain-service.disable" or "windows.prefetch.boot-only" or "windows.kernel-memory-policy" or "windows.game-content-indexing.disable") return ("basic", "PERFORMANCE TWEAKS");
		if (id == "windows.aero-shake.disable") return ("basic", "WINDOWS SETTINGS");
		if (id == "memory.compression.disable") return ("basic", "RAM TWEAKS");
		if (id == "gaming.game-bar-capture.disable") return ("basic", "GAME DVR");
		if (id.StartsWith("windows.repair.", StringComparison.Ordinal)) return ("basic", "DISC ERRORS");
		if (id is "processes.startup-quarantine" or "processes.task-scheduler-firewall") return ("processes", "STARTUP FIREWALL");
		if (id.StartsWith("processes.cleanup.", StringComparison.Ordinal)) return ("processes", "INSTANT CLEANUP");
		if (id.StartsWith("processes.", StringComparison.Ordinal)) return ("processes", "BACKGROUND SERVICES");
		if (id is "perf.visual-effects.best-performance" or "windows.tips-suggestions.disable"
			or "windows.search-web-suggestions.disable" or "network.throttling-index.disable")
			return ("basic", "WINDOWS SETTINGS");
		if (id.StartsWith("debloat.", StringComparison.OrdinalIgnoreCase)) return ("debloat", "BROWSER POLICIES");
		if (id == "gaming.game-mode.enable")
			return (Tab: "power", Group: "POWER TWEAKS");
		if (id == "gaming.mouse-acceleration.disable" || id.StartsWith("precision.", StringComparison.OrdinalIgnoreCase))
		{
			return (Tab: "precision", Group: "MOUSE & POINTER CONSISTENCY");
		}
		if (id.StartsWith("mitigations.", StringComparison.OrdinalIgnoreCase))
		{
			string item = ((id.Contains("smartscreen") || id.Contains("mark-of-the-web")) ? "APP & DOWNLOAD PROTECTIONS" : (id.Contains("cpu-side-channel") ? "CPU SIDE-CHANNEL MITIGATIONS" : "VIRTUALIZATION & KERNEL ISOLATION"));
			return (Tab: "mitigations", Group: item);
		}
		if (id.StartsWith("services.", StringComparison.OrdinalIgnoreCase))
		{
			return (Tab: "services", Group: "PROCESS LOWERING");
		}
		if (id.StartsWith("appearance.", StringComparison.OrdinalIgnoreCase) || id == "perf.visual-effects.best-performance" || id == "windows.file-extensions.show")
		{
			string item2 = ((id.Contains("explorer", StringComparison.OrdinalIgnoreCase) || id == "windows.file-extensions.show") ? "FILE EXPLORER" : ((id.Contains("context", StringComparison.OrdinalIgnoreCase) || id.Contains("previous-versions", StringComparison.OrdinalIgnoreCase)) ? "CONTEXT MENU" : "GENERAL"));
			return (Tab: "appearance", Group: item2);
		}
		if (tweak.Category == TweakCategory.Network)
		{
			string group = id.StartsWith("network.wifi.", StringComparison.Ordinal) ? "WIFI TWEAKS" : id.StartsWith("network.adapter.", StringComparison.Ordinal) ? "ADAPTER TWEAKS" : id switch
			{
				"advanced.nagle-algorithm.disable" or "network.netbios.disable" or "network.adapter-rss.enable" or "network.adapter.rsc.disable" or "network.adapter.lso.disable" => "ADAPTER TWEAKS",
				"network.smart-dns-resolution.disable" or "network.cloudflare-dns.ipv4" => "DNS & NAME RESOLUTION",
				_ => "NETWORK TWEAKS"
			};
			return (Tab: "network", Group: group);
		}
		if (tweak.Category == TweakCategory.Gpu)
		{
			string item3 = (id.StartsWith("gpu.nvidia.", StringComparison.OrdinalIgnoreCase) ? "NVIDIA DRIVER" : (id.StartsWith("gpu.amd.", StringComparison.OrdinalIgnoreCase) ? "AMD RADEON DRIVER" : "WINDOWS GPU SCHEDULING"));
			return (Tab: "gpu", Group: item3);
		}
		if (tweak.Category == TweakCategory.Cpu || id.StartsWith("power.", StringComparison.OrdinalIgnoreCase) || id == "perf.usb-selective-suspend" || id == "windows.fast-startup.disable")
		{
			string item4 = (id.Contains("usb", StringComparison.OrdinalIgnoreCase) ? "USB POWER" : ((id.Contains("hibernation", StringComparison.OrdinalIgnoreCase) || id.Contains("fast-startup", StringComparison.OrdinalIgnoreCase)) ? "HIBERNATION & STARTUP" : "CPU & TIMER POWER"));
			return (Tab: "power", Group: item4);
		}
		if (IsBasicTweak(id, tweak.Category))
		{
			return (Tab: "basic", Group: ResolveBasicGroup(id));
		}
		if (tweak.Category == TweakCategory.Windows)
		{
			return (Tab: "privacy", Group: ResolvePrivacyGroup(id));
		}
		return (Tab: "advanced", Group: tweak.Category switch
		{
			TweakCategory.Memory => "MEMORY & SCHEDULING", 
			TweakCategory.Storage => "STORAGE", 
			TweakCategory.Input => "INPUT", 
			TweakCategory.Gpu => "GPU SCHEDULING", 
			TweakCategory.Gaming => "GAME TASK SCHEDULING", 
			_ => "SYSTEM", 
		});
	}

	private static bool IsBasicTweak(string id, TweakCategory category)
	{
		bool flag = category == TweakCategory.Gaming;
		if (!flag)
		{
			bool flag2;
			switch (id)
			{
			case "gaming.mouse-acceleration.disable":
			case "windows.automatic-updates.disable":
			case "windows.update-pause.35days":
			case "perf.background-apps.disable":
			case "windows.crash-auto-restart.disable":
			case "windows.long-paths.enable":
			case "windows.recall-snapshots.disable":
			case "windows.storage-sense.disable":
			case "windows.settings-backup.disable":
			case "windows.defender-scan-cpu.target":
			case "windows.clipboard-history.disable":
			case "windows.widgets-news.disable":
			case "windows.fast-user-switching.enable":
			case "windows.store-auto-updates.disable":
			case "windows.audio-ducking.disable":
			case "windows.onedrive-sync.disable":
			case "windows.update-driver-delivery.disable":
			case "windows.menu-delay.reduce":
			case "windows.automatic-maintenance.disable":
			case "windows.sleep-study.disable":
			case "windows.accessibility-shortcut-prompts.disable":
				flag2 = true;
				break;
			default:
				flag2 = false;
				break;
			}
			flag = flag2;
		}
		return flag;
	}

	private static string ResolveBasicGroup(string id)
	{
		if (id.StartsWith("gaming.", StringComparison.OrdinalIgnoreCase))
		{
			return "GAMING";
		}
		if (id.Contains("update", StringComparison.OrdinalIgnoreCase))
		{
			return "UPDATES";
		}
		if (id.Contains("background", StringComparison.OrdinalIgnoreCase) || id.Contains("widgets", StringComparison.OrdinalIgnoreCase) || id.Contains("onedrive", StringComparison.OrdinalIgnoreCase))
		{
			return "BACKGROUND APPS";
		}
		if (id.Contains("maintenance", StringComparison.OrdinalIgnoreCase) || id.Contains("store-auto", StringComparison.OrdinalIgnoreCase) || id.Contains("sleep-study", StringComparison.OrdinalIgnoreCase))
		{
			return "MAINTENANCE & REPORTING";
		}
		if (id.Contains("accessibility", StringComparison.OrdinalIgnoreCase))
		{
			return "CONVENIENCE";
		}
		return "RESPONSIVENESS";
	}

	private static string ResolvePrivacyGroup(string id)
	{
		if (id.Contains("telemetry", StringComparison.OrdinalIgnoreCase) || id.Contains("ceip", StringComparison.OrdinalIgnoreCase) || id.Contains("feedback", StringComparison.OrdinalIgnoreCase) || id.Contains("app-launch", StringComparison.OrdinalIgnoreCase) || id.Contains("activity-history", StringComparison.OrdinalIgnoreCase) || id.Contains("error-reporting", StringComparison.OrdinalIgnoreCase))
		{
			return "TELEMETRY";
		}
		if (id.Contains("advertising", StringComparison.OrdinalIgnoreCase) || id.Contains("language-header", StringComparison.OrdinalIgnoreCase))
		{
			return "ADVERTISING";
		}
		if (id.Contains("typing", StringComparison.OrdinalIgnoreCase) || id.Contains("speech", StringComparison.OrdinalIgnoreCase) || id.Contains("input-personalization", StringComparison.OrdinalIgnoreCase))
		{
			return "INPUT PERSONALIZATION";
		}
		if (id.Contains("search", StringComparison.OrdinalIgnoreCase))
		{
			return "SEARCH SUGGESTIONS";
		}
		if (id.Contains("location", StringComparison.OrdinalIgnoreCase) || id.Contains("diagnostic-access", StringComparison.OrdinalIgnoreCase) || id.Contains("notification-access", StringComparison.OrdinalIgnoreCase))
		{
			return "APP PERMISSIONS";
		}
		if (id.Contains("lock-screen", StringComparison.OrdinalIgnoreCase))
		{
			return "LOCK SCREEN";
		}
		return "TAILORED EXPERIENCES";
	}
}












