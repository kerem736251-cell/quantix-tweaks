using System;
using System.Collections.Generic;

namespace QuantixTweakingUtility.Models;

public sealed class CleanupTarget
{
	public required string Id { get; init; }

	public required string Name { get; init; }

	public required string Description { get; init; }

	public IReadOnlyList<string> Paths { get; init; } = Array.Empty<string>();

	public bool IsCleanable { get; init; }

	public string UnavailableReason { get; init; } = string.Empty;
}
