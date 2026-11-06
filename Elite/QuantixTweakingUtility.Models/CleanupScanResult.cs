namespace QuantixTweakingUtility.Models;

public sealed class CleanupScanResult
{
	public required CleanupTarget Target { get; init; }

	public long ReclaimableBytes { get; init; }

	public string Status { get; init; } = "Ready";
}
