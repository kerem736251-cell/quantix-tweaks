using System.Collections.Generic;
using System.Threading.Tasks;
using QuantixTweakingUtility.Models;

namespace QuantixTweakingUtility.Services;

public interface IStartupManagerService
{
	Task<List<StartupItem>> GetStartupItemsAsync();

	Task<TweakActionResult> DisableAsync(StartupItem item);

	Task<TweakActionResult> EnableAsync(StartupItem item);
}
