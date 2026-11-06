namespace QuantixTweakingUtility.Models;

public sealed class CleanupActionResult
{
	public bool Success { get; init; }

	public long ReclaimedBytes { get; init; }

	public string Message { get; init; } = string.Empty;
}
