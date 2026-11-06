using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace QuantixTweakingUtility.Services;

public sealed class RestoreService : IRestoreService, IRestoreBackupMigrator
{
	private readonly object gate = new object();

	private readonly string path;

	private Dictionary<string, string> backups = new Dictionary<string, string>();

	public string? RecoveryWarning { get; private set; }

	public RestoreService()
		: this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Quantix", "restore-backup.json"))
	{
	}

	public RestoreService(string backupFilePath)
	{
		path = Path.GetFullPath(backupFilePath);
		try
		{
			backups = Load();
		}
		catch (Exception ex)
		{
			RecoveryWarning = "Restore backups could not be read. New changes are blocked to protect recovery data. " + ex.Message;
		}
	}

	private Dictionary<string, string> Load()
	{
		if (!File.Exists(path))
		{
			return new Dictionary<string, string>();
		}
		Dictionary<string, string>? obj = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path)) ?? throw new IOException("Backup file is empty or invalid.");
		if (obj.Any((KeyValuePair<string, string> x) => string.IsNullOrWhiteSpace(x.Key) || x.Value == null))
		{
			throw new IOException("Backup file has invalid entries.");
		}
		return obj;
	}

	private void EnsureHealthy()
	{
		if (RecoveryWarning != null)
		{
			throw new IOException(RecoveryWarning);
		}
	}

	public void SaveBackup(string tweakId, string previousValue)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(tweakId, "tweakId");
		ArgumentNullException.ThrowIfNull(previousValue, "previousValue");
		Change(delegate(Dictionary<string, string> values)
		{
			if (!values.ContainsKey(tweakId))
			{
				values.Add(tweakId, previousValue);
			}
		});
	}

	public void ClearBackup(string tweakId)
	{
		Change(delegate(Dictionary<string, string> values)
		{
			values.Remove(tweakId);
		});
	}

	public bool TryMigrateBackup(string tweakId, string expectedOriginal, string migratedOriginal)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(tweakId);
		ArgumentNullException.ThrowIfNull(expectedOriginal);
		ArgumentNullException.ThrowIfNull(migratedOriginal);
		bool migrated = false;
		Change(values =>
		{
			if (values.TryGetValue(tweakId, out var current) && current == expectedOriginal)
			{
				values[tweakId] = migratedOriginal;
				migrated = true;
			}
		});
		return migrated;
	}

	public string? GetBackup(string tweakId)
	{
		lock (gate)
		{
			EnsureHealthy();
			string value;
			return backups.TryGetValue(tweakId, out value) ? value : null;
		}
	}

	public IReadOnlyCollection<string> GetRestorableTweakIds()
	{
		lock (gate)
		{
			return (IReadOnlyCollection<string>)(object)backups.Keys.OrderBy((string x) => x).ToArray();
		}
	}

	private void Change(Action<Dictionary<string, string>> change)
	{
		lock (gate)
		{
			EnsureHealthy();
			Directory.CreateDirectory(Path.GetDirectoryName(path));
			using (new FileStream(path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
			{
				Dictionary<string, string> dictionary = Load();
				change(dictionary);
				string sourceFileName = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
				try
				{
					using (FileStream fileStream = new FileStream(sourceFileName, FileMode.CreateNew, FileAccess.Write, FileShare.None))
					{
						JsonSerializer.Serialize(fileStream, dictionary, new JsonSerializerOptions
						{
							WriteIndented = true
						});
						fileStream.Flush(flushToDisk: true);
					}
					if (File.Exists(path))
					{
						File.Replace(sourceFileName, path, path + ".previous");
					}
					else
					{
						File.Move(sourceFileName, path);
					}
					backups = dictionary;
				}
				finally
				{
					if (File.Exists(sourceFileName))
					{
						File.Delete(sourceFileName);
					}
				}
			}
		}
	}
}
