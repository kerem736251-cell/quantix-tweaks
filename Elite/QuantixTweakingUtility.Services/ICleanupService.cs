using System.Collections.Generic;
using System.Threading.Tasks;
using QuantixTweakingUtility.Models;

namespace QuantixTweakingUtility.Services;

public interface ICleanupService
{
	IReadOnlyList<CleanupTarget> GetTargets();

	Task<IReadOnlyList<CleanupScanResult>> ScanAsync();

	Task<CleanupActionResult> CleanAsync(IEnumerable<string> targetIds);
}
