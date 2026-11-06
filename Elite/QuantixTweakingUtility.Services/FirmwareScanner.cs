using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;

namespace QuantixTweakingUtility.Services;

public static class FirmwareScanner
{
	public static FirmwareSnapshot Scan()
	{
		List<string> issues = new List<string>();
		Dictionary<string, object> row = Read("Win32_BaseBoard", "Manufacturer,Product,Version", issues).FirstOrDefault();
		List<Dictionary<string, object>> list = Read("Win32_Processor", "Name", issues);
		Dictionary<string, object> row2 = Read("Win32_BIOS", "SMBIOSBIOSVersion", issues).FirstOrDefault();
		List<Dictionary<string, object>> source = Read("Win32_PhysicalMemory", "PartNumber,Capacity,ConfiguredClockSpeed,SMBIOSMemoryType", issues);
		return new FirmwareSnapshot
		{
			Manufacturer = Text(row, "Manufacturer"),
			Board = Text(row, "Product") + " / revision " + Text(row, "Version"),
			Cpu = ((list.Count == 0) ? "Not reported" : string.Join(" / ", list.Select((Dictionary<string, object> c) => Text(c, "Name")))),
			Bios = Text(row2, "SMBIOSBIOSVersion"),
			Issues = issues,
			Modules = source.Select((Dictionary<string, object> m) => new MemoryModule(Text(m, "PartNumber"), Number(m, "Capacity"), (uint)Number(m, "ConfiguredClockSpeed"), (uint)Number(m, "SMBIOSMemoryType"))).ToList()
		};
	}

	private static List<Dictionary<string, object>> Read(string type, string fields, List<string> issues)
	{
		List<Dictionary<string, object>> list = new List<Dictionary<string, object>>();
		try
		{
			using ManagementObjectSearcher managementObjectSearcher = new ManagementObjectSearcher("root\\cimv2", "SELECT " + fields + " FROM " + type);
			managementObjectSearcher.Options.Timeout = TimeSpan.FromSeconds(5.0);
			using ManagementObjectCollection managementObjectCollection = managementObjectSearcher.Get();
			foreach (ManagementObject item in managementObjectCollection)
			{
				using (item)
				{
					Dictionary<string, object> dictionary = new Dictionary<string, object>();
					string[] array = fields.Split(',');
					foreach (string text in array)
					{
						dictionary[text] = item[text];
					}
					list.Add(dictionary);
				}
			}
			if (list.Count == 0)
			{
				issues.Add(type + ": no inventory returned.");
			}
		}
		catch (Exception)
		{
			issues.Add(type + ": inventory unavailable. Check Windows Management Instrumentation and rescan.");
		}
		return list;
	}

	private static string Text(Dictionary<string, object>? row, string name)
	{
		object value;
		string text = ((row == null || !row.TryGetValue(name, out value)) ? null : Convert.ToString(value)?.Trim());
		if (!string.IsNullOrWhiteSpace(text) && !text.Equals("To be filled by O.E.M.", StringComparison.OrdinalIgnoreCase) && !text.Equals("Default string", StringComparison.OrdinalIgnoreCase))
		{
			return text;
		}
		return "Not reported";
	}

	private static ulong Number(Dictionary<string, object> row, string name)
	{
		if (!row.TryGetValue(name, out var value) || !ulong.TryParse(Convert.ToString(value), out var result))
		{
			return 0uL;
		}
		return result;
	}

	public static string SupportUrl(string manufacturer)
	{
		string text = manufacturer.ToUpperInvariant();
		if (text.Contains("MICRO-STAR") || text == "MSI")
		{
			return "https://www.msi.com/support";
		}
		if (text.Contains("ASUSTEK") || text.Contains("ASUS"))
		{
			return "https://www.asus.com/support/";
		}
		if (text.Contains("GIGABYTE"))
		{
			return "https://www.gigabyte.com/Support";
		}
		if (text.Contains("ASROCK"))
		{
			return "https://www.asrock.com/support/";
		}
		if (text.Contains("DELL"))
		{
			return "https://www.dell.com/support/home";
		}
		if (text.Contains("LENOVO"))
		{
			return "https://support.lenovo.com/";
		}
		if (text.Contains("HEWLETT") || text == "HP")
		{
			return "https://support.hp.com/";
		}
		return "";
	}

	public static IReadOnlyList<FirmwareReview> Review(FirmwareSnapshot scan)
	{
		List<FirmwareReview> list = new List<FirmwareReview>();
		if (scan.Modules.Count > 0)
		{
			list.Add(new FirmwareReview("Memory profile review", "Configured memory speed is reported by firmware. It does not identify the active XMP/EXPO profile or the kit's rated profile. Confirm the exact kit and motherboard support before enabling memory overclocking. Memory settings have not been changed."));
		}
		if (scan.Cpu.Contains("X3D", StringComparison.OrdinalIgnoreCase))
		{
			list.Add(new FirmwareReview("X3D processor detected", "No generic voltage, multiplier or Curve Optimizer values are prescribed. Support varies by exact processor and BIOS. Use supported manufacturer controls and validate stability before keeping a change."));
		}
		else if (scan.Cpu.Contains("Ryzen", StringComparison.OrdinalIgnoreCase))
		{
			list.Add(new FirmwareReview("AMD Ryzen tuning", "Ryzen Master offers tuning on supported processors. Detection alone does not confirm which controls are supported. No automatic PBO, voltage or thermal-limit changes are made."));
		}
		else if (scan.Cpu.Contains("Intel", StringComparison.OrdinalIgnoreCase))
		{
			list.Add(new FirmwareReview("Intel processor review", "Check the exact system's firmware and Intel default settings before considering tuning. Quantix does not infer overclocking support from the Intel name or remove power and thermal limits."));
		}
		else
		{
			list.Add(new FirmwareReview("Processor compatibility unknown", "No tuning profile can be selected from this inventory. Use the exact system manufacturer's documentation."));
		}
		list.Add(new FirmwareReview("Firmware compatibility", "Installed BIOS: " + scan.Bios + ". Check your exact model and revision on the manufacturer's support page for release notes. This scan does not determine whether a newer BIOS exists and does not flash firmware."));
		return list;
	}
}
