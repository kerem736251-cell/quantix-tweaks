using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using QuantixTweakingUtility.Models;

namespace QuantixTweakingUtility.Services;

public static class StabilityPolicy
{
	public const string Message = "This driver override has been retired for stability. Restore the saved original values instead, then restart Windows.";

	private static readonly HashSet<string> Retired = new HashSet<string>(StringComparer.Ordinal) { "gpu.nvidia.write-combining.disable", "gpu.nvidia.preemption.disable", "gpu.nvidia.hidden-power-saving.disable", "gpu.nvidia.p0-state.enable", "gpu.nvidia.clock-gating.disable", "gpu.amd.preemption.disable" };

	public static TweakDefinition Protect(TweakDefinition original, Func<bool> hasBackup)
	{
		if (!Retired.Contains(original.Id))
		{
			return original;
		}
		return new TweakDefinition
		{
			Id = original.Id,
			Name = original.Name + " (restore only)",
			Description = "This driver override has been retired for stability. Restore the saved original values instead, then restart Windows.",
			Category = original.Category,
			RiskLevel = original.RiskLevel,
			RequiresAdministrator = original.RequiresAdministrator,
			RequiresRestart = true,
			RestoreOnly = true,
			HasSavedOriginal = hasBackup,
			AffectedSettings = original.AffectedSettings,
			CurrentStateReader = () => Task.FromResult(hasBackup() ? TweakState.Applied : TweakState.NotApplied),
			ApplyAction = () => Task.FromResult(TweakActionResult.Fail("This driver override has been retired for stability. Restore the saved original values instead, then restart Windows.")),
			RestoreAction = () => (!hasBackup()) ? Task.FromResult(TweakActionResult.Fail("No saved original values exist. No changes were made.")) : original.RestoreAction()
		};
	}
}
