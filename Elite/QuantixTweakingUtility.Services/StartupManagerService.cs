using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Win32;
using QuantixTweakingUtility.Models;

namespace QuantixTweakingUtility.Services;

public sealed class StartupManagerService : IStartupManagerService
{
	private sealed class BackupEntry
	{
		public string Name { get; set; } = string.Empty;

		public string CommandOrPath { get; set; } = string.Empty;

		public StartupSource Source { get; set; }

		public string? StoredShortcutPath { get; set; }
	}

	private const string RunKeyPath = "Software\\Microsoft\\Windows\\CurrentVersion\\Run";

	private readonly ILoggingService _logging;

	private readonly string _backupFilePath;

	private readonly string _disabledShortcutsDir;

	public StartupManagerService(ILoggingService logging)
	{
		_logging = logging;
		string text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Quantix");
		Directory.CreateDirectory(text);
		_backupFilePath = Path.Combine(text, "StartupBackups.json");
		_disabledShortcutsDir = Path.Combine(text, "DisabledStartupShortcuts");
		Directory.CreateDirectory(_disabledShortcutsDir);
	}

	public Task<List<StartupItem>> GetStartupItemsAsync()
	{
		return Task.Run(delegate
		{
			List<StartupItem> list = new List<StartupItem>();
			AddRegistryItems(list, Registry.CurrentUser, StartupSource.CurrentUserRun);
			AddRegistryItems(list, Registry.LocalMachine, StartupSource.LocalMachineRun);
			AddFolderItems(list, Environment.GetFolderPath(Environment.SpecialFolder.Startup));
			AddFolderItems(list, Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup));
			foreach (BackupEntry backup in LoadBackups().Values)
			{
				if (!list.Any((StartupItem i) => i.Name.Equals(backup.Name, StringComparison.OrdinalIgnoreCase) && i.Source == backup.Source))
				{
					list.Add(new StartupItem
					{
						Name = backup.Name,
						CommandOrPath = backup.CommandOrPath,
						Source = backup.Source,
						IsEnabled = false
					});
				}
			}
			return list.OrderBy<StartupItem, string>((StartupItem i) => i.Name, StringComparer.OrdinalIgnoreCase).ToList();
		});
	}

	public Task<TweakActionResult> DisableAsync(StartupItem item)
	{
		return Task.Run(delegate
		{
			try
			{
				if (item.Source == StartupSource.StartupFolder)
				{
					if (!File.Exists(item.CommandOrPath))
					{
						return TweakActionResult.Fail("That shortcut no longer exists.");
					}
					string text = Path.Combine(_disabledShortcutsDir, Path.GetFileName(item.CommandOrPath));
					File.Move(item.CommandOrPath, text, overwrite: true);
					SaveBackup(item, text);
				}
				else
				{
					using RegistryKey registryKey = ((item.Source == StartupSource.CurrentUserRun) ? Registry.CurrentUser : Registry.LocalMachine).OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run", writable: true);
					registryKey?.DeleteValue(item.Name, throwOnMissingValue: false);
					SaveBackup(item, null);
				}
				_logging.Info("StartupManager", "Disabled startup item: " + item.Name);
				return TweakActionResult.Ok(item.Name + " disabled. It will no longer launch at sign-in.");
			}
			catch (Exception ex)
			{
				_logging.Error("StartupManager", "Failed to disable startup item " + item.Name + ".", ex);
				return TweakActionResult.Fail("Could not disable that startup item - it may require administrator rights depending on where it's registered.");
			}
		});
	}

	public Task<TweakActionResult> EnableAsync(StartupItem item)
	{
		return Task.Run(delegate
		{
			if (!LoadBackups().TryGetValue(BackupKey(item), out var value))
			{
				return TweakActionResult.Fail("No Quantix backup was found for that item - it may not have been disabled by Quantix.");
			}
			try
			{
				if (item.Source == StartupSource.StartupFolder)
				{
					string storedShortcutPath = value.StoredShortcutPath;
					if (string.IsNullOrEmpty(storedShortcutPath) || !File.Exists(storedShortcutPath))
					{
						return TweakActionResult.Fail("The saved shortcut file could not be found.");
					}
					File.Move(storedShortcutPath, value.CommandOrPath, overwrite: true);
				}
				else
				{
					using RegistryKey registryKey = ((item.Source == StartupSource.CurrentUserRun) ? Registry.CurrentUser : Registry.LocalMachine).CreateSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run", writable: true);
					registryKey?.SetValue(value.Name, value.CommandOrPath, RegistryValueKind.String);
				}
				RemoveBackup(item);
				_logging.Info("StartupManager", "Re-enabled startup item: " + item.Name);
				return TweakActionResult.Ok(item.Name + " will launch at sign-in again.");
			}
			catch (Exception ex)
			{
				_logging.Error("StartupManager", "Failed to re-enable startup item " + item.Name + ".", ex);
				return TweakActionResult.Fail("Could not re-enable that startup item.");
			}
		});
	}

	private static void AddRegistryItems(List<StartupItem> items, RegistryKey hive, StartupSource source)
	{
		try
		{
			using RegistryKey registryKey = hive.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run", writable: false);
			if (registryKey == null)
			{
				return;
			}
			string[] valueNames = registryKey.GetValueNames();
			foreach (string text in valueNames)
			{
				if (!string.IsNullOrWhiteSpace(text))
				{
					string commandOrPath = (registryKey.GetValue(text) as string) ?? string.Empty;
					items.Add(new StartupItem
					{
						Name = text,
						CommandOrPath = commandOrPath,
						Source = source,
						IsEnabled = true
					});
				}
			}
		}
		catch
		{
		}
	}

	private static void AddFolderItems(List<StartupItem> items, string folderPath)
	{
		if (string.IsNullOrEmpty(folderPath) || !Directory.Exists(folderPath))
		{
			return;
		}
		foreach (string item in Directory.EnumerateFiles(folderPath, "*.lnk"))
		{
			items.Add(new StartupItem
			{
				Name = Path.GetFileNameWithoutExtension(item),
				CommandOrPath = item,
				Source = StartupSource.StartupFolder,
				IsEnabled = true
			});
		}
	}

	private static string BackupKey(StartupItem item)
	{
		return $"{item.Source}:{item.Name}";
	}

	private Dictionary<string, BackupEntry> LoadBackups()
	{
		try
		{
			if (File.Exists(_backupFilePath))
			{
				Dictionary<string, BackupEntry> dictionary = JsonSerializer.Deserialize<Dictionary<string, BackupEntry>>(File.ReadAllText(_backupFilePath));
				if (dictionary != null)
				{
					return dictionary;
				}
			}
		}
		catch
		{
		}
		return new Dictionary<string, BackupEntry>();
	}

	private void SaveBackup(StartupItem item, string? storedShortcutPath)
	{
		Dictionary<string, BackupEntry> dictionary = LoadBackups();
		dictionary[BackupKey(item)] = new BackupEntry
		{
			Name = item.Name,
			CommandOrPath = item.CommandOrPath,
			Source = item.Source,
			StoredShortcutPath = storedShortcutPath
		};
		Persist(dictionary);
	}

	private void RemoveBackup(StartupItem item)
	{
		Dictionary<string, BackupEntry> dictionary = LoadBackups();
		dictionary.Remove(BackupKey(item));
		Persist(dictionary);
	}

	private void Persist(Dictionary<string, BackupEntry> backups)
	{
		try
		{
			string contents = JsonSerializer.Serialize(backups, new JsonSerializerOptions
			{
				WriteIndented = true
			});
			File.WriteAllText(_backupFilePath, contents);
		}
		catch
		{
		}
	}
}
