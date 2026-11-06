using System.Collections.Generic;
using System.Threading.Tasks;
using QuantixTweakingUtility.Models;

namespace QuantixTweakingUtility.Services;

public interface ITweakService
{
	IReadOnlyList<TweakDefinition> GetAllTweaks();

	IReadOnlyList<TweakDefinition> GetTweaksByCategory(TweakCategory category);

	Task<TweakActionResult> ApplyTweakAsync(string tweakId);

	Task<TweakActionResult> RestoreTweakAsync(string tweakId);

	Task<List<TweakDefinition>> GetRecommendedTweaksAsync();
}
