using System;

namespace QuantixTweakingUtility.Models;

public sealed class SystemSnapshot
{
	public string CpuName { get; set; } = "Detecting...";

	public string GpuName { get; set; } = "Detecting...";

	public string RamTotalDisplay { get; set; } = "Detecting...";

	public string OsDisplay { get; set; } = "Detecting...";

	public TimeSpan Uptime { get; set; }
}
