using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Win32;
using QuantixTweakingUtility.Models;

namespace QuantixTweakingUtility.Services;

public sealed class GameLibraryService : IGameLibraryService
{
	private readonly ILoggingService _logging;

	public GameLibraryService(ILoggingService logging)
	{
		_logging = logging;
	}

	public async Task<List<DetectedGame>> ScanForInstalledGamesAsync()
	{
		List<DetectedGame> results = new List<DetectedGame>();
		await Task.Run(delegate
		{
			try
			{
				results.AddRange(ScanSteam());
			}
			catch (Exception ex)
			{
				_logging.Error("GameLibraryService", "Steam scan failed.", ex);
			}
			try
			{
				results.AddRange(ScanEpicGames());
			}
			catch (Exception ex2)
			{
				_logging.Error("GameLibraryService", "Epic Games scan failed.", ex2);
			}
		});
		return results.OrderBy<DetectedGame, string>((DetectedGame g) => g.Name, StringComparer.OrdinalIgnoreCase).ToList();
	}

	private List<DetectedGame> ScanSteam()
	{
		List<DetectedGame> list = new List<DetectedGame>();
		string text = ReadSteamInstallPath();
		if (text == null || !Directory.Exists(text))
		{
			return list;
		}
		List<string> list2 = new List<string> { text };
		string path = Path.Combine(text, "steamapps", "libraryfolders.vdf");
		if (File.Exists(path))
		{
			try
			{
				foreach (Match item in Regex.Matches(File.ReadAllText(path), "\"path\"\\s*\"([^\"]+)\""))
				{
					string text2 = item.Groups[1].Value.Replace("\\\\", "\\");
					if (Directory.Exists(text2) && !list2.Contains<string>(text2, StringComparer.OrdinalIgnoreCase))
					{
						list2.Add(text2);
					}
				}
			}
			catch (Exception ex)
			{
				_logging.Error("GameLibraryService", "Failed reading libraryfolders.vdf.", ex);
			}
		}
		foreach (string item2 in list2)
		{
			string text3 = Path.Combine(item2, "steamapps");
			if (!Directory.Exists(text3))
			{
				continue;
			}
			foreach (string item3 in Directory.EnumerateFiles(text3, "appmanifest_*.acf"))
			{
				try
				{
					string input = File.ReadAllText(item3);
					Match match = Regex.Match(input, "\"name\"\\s*\"([^\"]+)\"");
					Match match2 = Regex.Match(input, "\"installdir\"\\s*\"([^\"]+)\"");
					if (match.Success)
					{
						string installPath = (match2.Success ? Path.Combine(text3, "common", match2.Groups[1].Value) : text3);
						list.Add(new DetectedGame
						{
							Name = match.Groups[1].Value,
							Source = "Steam",
							InstallPath = installPath
						});
					}
				}
				catch
				{
				}
			}
		}
		return list;
	}

	private static string? ReadSteamInstallPath()
	{
		try
		{
			using RegistryKey registryKey = Registry.CurrentUser.OpenSubKey("Software\\Valve\\Steam");
			string text = registryKey?.GetValue("SteamPath") as string;
			return string.IsNullOrWhiteSpace(text) ? null : text.Replace('/', '\\');
		}
		catch
		{
			return null;
		}
	}

	private List<DetectedGame> ScanEpicGames()
	{
		List<DetectedGame> list = new List<DetectedGame>();
		string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Epic", "EpicGamesLauncher", "Data", "Manifests");
		if (!Directory.Exists(path))
		{
			return list;
		}
		foreach (string item in Directory.EnumerateFiles(path, "*.item"))
		{
			try
			{
				using JsonDocument jsonDocument = JsonDocument.Parse(File.ReadAllText(item));
				JsonElement rootElement = jsonDocument.RootElement;
				if (rootElement.TryGetProperty("DisplayName", out var value))
				{
					JsonElement value2;
					string installPath = (rootElement.TryGetProperty("InstallLocation", out value2) ? (value2.GetString() ?? string.Empty) : string.Empty);
					list.Add(new DetectedGame
					{
						Name = (value.GetString() ?? "Unknown"),
						Source = "Epic Games",
						InstallPath = installPath
					});
				}
			}
			catch
			{
			}
		}
		return list;
	}
}
