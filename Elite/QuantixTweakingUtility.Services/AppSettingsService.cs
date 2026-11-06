using System;
using System.IO;
using System.Text.Json;
using Microsoft.Win32;
using QuantixTweakingUtility.Models;

namespace QuantixTweakingUtility.Services;

public sealed class AppSettingsService : IAppSettingsService
{
	private const string StartupRegistryPath = "Software\\Microsoft\\Windows\\CurrentVersion\\Run";

	private const string StartupValueName = "QuantixTweakingUtility";

	private readonly string _settingsPath;

	private readonly ILoggingService _logging;

	public AppSettings Settings { get; }

	public AppSettingsService(ILoggingService logging)
	{
		_logging = logging;
		string text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Quantix");
		Directory.CreateDirectory(text);
		_settingsPath = Path.Combine(text, "settings.json");
		Settings = Load();
	}

	public bool IsLaunchAtSignInEnabled()
	{
		try
		{
			using RegistryKey registryKey = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run", writable: false);
			return registryKey?.GetValue("QuantixTweakingUtility") is string value && !string.IsNullOrWhiteSpace(value);
		}
		catch (Exception ex)
		{
			_logging.Error("Settings", "Could not read the startup setting.", ex);
			return false;
		}
	}

	public void SetLaunchAtSignIn(bool enabled)
	{
		try
		{
			using RegistryKey registryKey = Registry.CurrentUser.CreateSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run", writable: true) ?? throw new InvalidOperationException("Could not access the Windows startup registry key.");
			if (enabled)
			{
				string processPath = Environment.ProcessPath;
				if (string.IsNullOrWhiteSpace(processPath))
				{
					throw new InvalidOperationException("Quantix could not determine its executable path.");
				}
				registryKey.SetValue("QuantixTweakingUtility", "\"" + processPath + "\"");
			}
			else
			{
				registryKey.DeleteValue("QuantixTweakingUtility", throwOnMissingValue: false);
			}
			_logging.Info("Settings", enabled ? "Enabled launch at sign-in." : "Disabled launch at sign-in.");
		}
		catch (Exception ex)
		{
			_logging.Error("Settings", "Could not update the startup setting.", ex);
			throw new InvalidOperationException("Windows could not update the launch-at-sign-in setting.", ex);
		}
	}

	public void Save()
	{
		string text = _settingsPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
		try
		{
			File.WriteAllText(text, JsonSerializer.Serialize(Settings, new JsonSerializerOptions
			{
				WriteIndented = true
			}));
			if (File.Exists(_settingsPath))
			{
				File.Replace(text, _settingsPath, _settingsPath + ".previous");
			}
			else
			{
				File.Move(text, _settingsPath);
			}
		}
		catch (Exception ex)
		{
			_logging.Error("Settings", "Could not save application settings.", ex);
			throw new IOException("Application preferences could not be saved.", ex);
		}
		finally
		{
			if (File.Exists(text))
			{
				File.Delete(text);
			}
		}
	}

	private AppSettings Load()
	{
		try
		{
			if (File.Exists(_settingsPath))
			{
				return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_settingsPath)) ?? new AppSettings();
			}
		}
		catch (Exception ex)
		{
			_logging.Error("Settings", "Could not read application settings.", ex);
		}
		return new AppSettings();
	}
}
