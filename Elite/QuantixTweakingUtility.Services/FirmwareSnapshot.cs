using System;
using System.Collections.Generic;
using System.Linq;

namespace QuantixTweakingUtility.Services;

public sealed class FirmwareSnapshot
{
	public string Board { get; init; } = "Not reported";

	public string Manufacturer { get; init; } = "Not reported";

	public string Cpu { get; init; } = "Not reported";

	public string Bios { get; init; } = "Not reported";

	public List<MemoryModule> Modules { get; init; } = new List<MemoryModule>();

	public List<string> Issues { get; init; } = new List<string>();

	public string Memory
	{
		get
		{
			if (Modules.Count != 0)
			{
				return $"{((IEnumerable<MemoryModule>)Modules).Sum((Func<MemoryModule, double>)((MemoryModule m) => m.Capacity)) / 1073741824.0:0.#} GB / {Modules.Count} modules";
			}
			return "Not reported";
		}
	}

	public string MemoryDetails => string.Join("\n", Modules.Select((MemoryModule m) => $"{m.Part} / {(double)m.Capacity / 1073741824.0:0.#} GB / {((m.Type == 34) ? "DDR5" : ((m.Type == 26) ? "DDR4" : "type not identified"))} / configured speed: {((m.ConfiguredSpeed == 0) ? "not reported" : m.ConfiguredSpeed.ToString())}"));

	public string SupportUrl => FirmwareScanner.SupportUrl(Manufacturer);

	public string SupportStatus => "Scroll down to BIOS tweaks to read exposed settings with your compatible AMI package. Hardware detection alone does not establish tuning compatibility.";
}
