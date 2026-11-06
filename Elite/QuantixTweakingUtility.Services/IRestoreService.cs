using System.Collections.Generic;

namespace QuantixTweakingUtility.Services;

public interface IRestoreBackupMigrator
{
	bool TryMigrateBackup(string tweakId, string expectedOriginal, string migratedOriginal);
}

public interface IRestoreService
{
	void SaveBackup(string tweakId, string previousValue);

	string? GetBackup(string tweakId);

	void ClearBackup(string tweakId);

	IReadOnlyCollection<string> GetRestorableTweakIds();
}
