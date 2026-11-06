using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace QuantixTweakingUtility.Models;

public sealed class TweakDefinition
{
	public string Id { get; set; }

	public string Name { get; set; }

	public string Description { get; set; }

	public TweakCategory Category { get; set; }

	public TweakRiskLevel RiskLevel { get; set; }

	public bool RequiresAdministrator { get; set; }

	public bool RequiresRestart { get; set; }

	public bool RestoreOnly { get; set; }

	public bool IsOneTimeAction { get; set; }

	public Func<bool>? HasSavedOriginal { get; set; }

	public IReadOnlyList<string> AffectedSettings { get; set; } = Array.Empty<string>();

	public Func<Task<TweakState>> CurrentStateReader { get; set; }

	public Func<Task<TweakActionResult>> ApplyAction { get; set; }

	public Func<Task<TweakActionResult>> RestoreAction { get; set; }
}
