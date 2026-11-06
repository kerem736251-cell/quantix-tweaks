using System;
using System.Threading.Tasks;

namespace QuantixTweakingUtility.Services;

public static class QuantixPowerSettings
{
	public const string Description = "Creates a Quantix custom gaming plan: CPU performance range 100%, PCIe link power saving off, display timeout Never (including the lock screen), and automatic sleep/hibernate Never on AC and battery. Uses more power and may increase heat and battery drain. Restoring returns to your previously active plan. FPS gains depend on hardware and game.";

	public static readonly string[] Settings = new string[7] { "SUB_PROCESSOR PROCTHROTTLEMIN 100", "SUB_PROCESSOR PROCTHROTTLEMAX 100", "SUB_PCIEXPRESS ASPM 0", "SUB_VIDEO VIDEOIDLE 0", "SUB_VIDEO VIDEOCONLOCK 0", "SUB_SLEEP STANDBYIDLE 0", "SUB_SLEEP HIBERNATEIDLE 0" };

	public static async Task<bool> ConfigureAsync(string guid, Func<string, Task<bool>> run)
	{
		if (!Guid.TryParse(guid, out var _))
		{
			return false;
		}
		string[] array = new string[2] { "/setacvalueindex", "/setdcvalueindex" };
		foreach (string mode in array)
		{
			string[] settings = Settings;
			foreach (string text in settings)
			{
				if (!(await run(mode + " " + guid + " " + text)))
				{
					return false;
				}
			}
		}
		return true;
	}
}
