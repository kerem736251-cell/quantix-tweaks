namespace QuantixTweakingUtility.Models;

public sealed class DetectedGame
{
	public required string Name { get; init; }

	public required string Source { get; init; }

	public required string InstallPath { get; init; }
}
