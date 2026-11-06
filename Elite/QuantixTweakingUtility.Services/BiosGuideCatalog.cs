using System;
using System.Text;
using QuantixTweakingUtility.ViewModels;

namespace QuantixTweakingUtility.Services;

public static class BiosGuideCatalog
{
	public static readonly string[] Platforms = new string[13]
	{
		"Select your CPU platform", "AMD AM4 — Ryzen 1000 / 2000 / unknown", "AMD AM4 — Ryzen 3000 (not 3200G / 3400G)", "AMD AM4 — Ryzen 5000 (non-X3D)", "AMD AM4 — Ryzen 5000 X3D", "AMD AM5 — Ryzen 7000 / 9000 (non-X3D)", "AMD AM5 — Ryzen 7000 X3D", "AMD AM5 — Ryzen 9000 X3D", "AMD AM5 — Ryzen 8000G / other / unsure", "Intel Core desktop — 12th generation",
		"Intel Core desktop — 13th / 14th generation", "Intel Core Ultra desktop — Series 2", "Other / mobile / unsure"
	};

	public static string Build(string brand, string platform, string board, string cpu, bool memory, bool pboConfirmed, bool cStateExperiment)
	{
		StringBuilder s = new StringBuilder();
		s.AppendLine("QUANTIX / BIOS TWEAK PATHS");
		StringBuilder stringBuilder = s;
		StringBuilder stringBuilder2 = stringBuilder;
		StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(10, 2, stringBuilder);
		handler.AppendLiteral("Board: ");
		handler.AppendFormatted(brand);
		handler.AppendLiteral(" / ");
		handler.AppendFormatted(string.IsNullOrWhiteSpace(board) ? "model not specified" : board.Trim());
		stringBuilder2.AppendLine(ref handler);
		stringBuilder = s;
		StringBuilder stringBuilder3 = stringBuilder;
		handler = new StringBuilder.AppendInterpolatedStringHandler(5, 1, stringBuilder);
		handler.AppendLiteral("CPU: ");
		handler.AppendFormatted(string.IsNullOrWhiteSpace(cpu) ? "model not specified" : cpu.Trim());
		stringBuilder3.AppendLine(ref handler);
		s.AppendLine(platform);
		s.AppendLine("Menu paths below are examples from vendor BIOS families, NOT verified for your exact model/version. Skip missing options. Model text is a label, not hardware validation. This checklist does not apply settings. The separate SCEWIN workflow can import explicitly reviewed experiments on exposed firmware options.");
		s.AppendLine("Before changing anything: photograph original values, keep your BitLocker recovery key available and check the exact board manual. Change one option, test, then continue. No guaranteed FPS gain.");
		if (((ReadOnlySpan<string>)Platforms).Contains(platform))
		{
			object obj = new string[4] { "ASUS", "MSI", "Gigabyte", "ASRock" };
			if (new ReadOnlySpan<string>((string[]?)obj).Contains(brand) && !platform.StartsWith("Select") && !platform.StartsWith("Other"))
			{
				bool flag = platform.StartsWith("AMD");
				bool flag2 = platform.Contains("AM5");
				bool flag3 = platform.Contains("1000") || platform.Contains("8000G");
				bool flag4 = platform.Contains("5000 X3D");
				bool flag5 = platform.Contains("13th / 14th");
				s.AppendLine("\nCPU / BOOST");
				if (flag)
				{
					string text = brand switch
					{
						"ASUS" => "Advanced --> AMD CBS", 
						"MSI" => "OC --> Advanced CPU Configuration --> AMD CBS", 
						"Gigabyte" => "Tweaker --> Advanced CPU Settings", 
						_ => "Advanced --> AMD CBS --> CPU Common Options", 
					};
					string text2 = ((brand == "MSI") ? "OC --> Advanced CPU Configuration --> AMD Overclocking --> Precision Boost Overdrive" : ((!(brand == "Gigabyte")) ? "Advanced --> AMD Overclocking --> Precision Boost Overdrive" : "Tweaker --> Advanced CPU Settings --> Precision Boost Overdrive"));
					if (!flag2)
					{
						s.AppendLine("AM4: newer-family paths are navigation hints only. Older BIOS may use CPU Features or Zen Common Options; confirm in your manual.");
					}
					Row(text + " --> Core Performance Boost", "Auto / Enabled (if offered)", "Allows normal automatic CPU boost. If already active, no additional benefit is expected. Keep thermal protection active.");
					bool flag6 = !flag3 && !flag4;
					Row(text2, (flag6 && pboConfirmed) ? "Enabled (optional overclocking trial)" : "Keep current/default setting — support not confirmed", flag6 ? "Only with confirmed CPU + chipset + BIOS support and adequate cooling. PBO relaxes stock operating limits; heat/power can rise and gains vary. AMD warns this operation is outside specifications and invalidates its product warranty. If Enabled is absent, do not substitute a vendor enhanced preset." : "This CPU group gets no generic PBO-enable instruction. Check model-specific support; a visible AMD menu does not establish compatibility.");
					if (flag6 && pboConfirmed)
					{
						s.AppendLine("Leave scalar, boost override and manual voltage at default; do not select Motherboard/unlimited limits or copy another CPU's PPT/TDC/EDC. Restore the photographed PBO value if performance or stability worsens.");
					}
					if (!flag3 && !platform.Contains("3000"))
					{
						Row(text2 + " --> Curve Optimizer", "Optional per-core tuning — no universal value", flag4 ? "5000 X3D: availability depends on model and BIOS. Do not copy AM5 voltage or frequency overrides. Leave unchanged without exact-board guidance." : "On supported CPUs, negative offsets can reduce requested voltage, but excessive offsets cause errors, idle crashes or lost performance. Begin at default; tune/test individual cores, load and idle. Never apply a blanket -30 preset.");
					}
					if (platform.Contains("7000 X3D"))
					{
						s.AppendLine("7000 X3D: keep manual multiplier and core voltage at stock. Do not copy 9000 X3D overclocking controls; dual-CCD models also need the correct AMD chipset driver.");
					}
					if (platform.Contains("9000 X3D"))
					{
						s.AppendLine("9000 X3D: tuning support differs from earlier X3D. This guide still supplies no manual voltage or boost-offset target; use exact-CPU specifications.");
					}
					Row(text + " --> Global C-state Control", cStateExperiment ? "Disabled (comparison test only)" : "Auto / Enabled", "Controls idle power states. Disabling can increase idle power/heat and alter boost behavior; it is not a proven universal latency fix. Compare repeated frametime tests against the original value and revert if there is no measurable benefit.");
				}
				else
				{
					string obj2 = ((brand == "MSI") ? "OC --> Advanced CPU Configuration" : ((!(brand == "Gigabyte")) ? "BIOS search / exact manual --> CPU power-management options" : "Tweaker --> Advanced CPU Settings"));
					s.AppendLine("Intel has no AMD PBO. Some control names differ by firmware; search terms are marked where a family-specific path is not verified.");
					if (platform.Contains("Series 2"))
					{
						s.AppendLine("Core Ultra Series 2: do not reuse 12th–14th generation ratios, voltage or power limits. Menu examples must be checked against the Intel 800-series board manual.");
					}
					Row(obj2 + " --> Intel Turbo Boost", "Enabled (if supported)", "Permits stock turbo within CPU power and thermal limits. Keep vendor/Intel default limits; do not use unlimited power.");
					Row(obj2 + " --> Intel Speed Shift Technology", "Enabled (if supported)", "Allows hardware-managed performance-state transitions. This is normally already enabled; changing an already enabled setting adds no performance.");
					string text3 = ((brand == "MSI") ? "Intel C-State" : ((brand == "Gigabyte") ? "C-States Control" : "CPU C-States"));
					Row(obj2 + " --> " + text3, (cStateExperiment && !flag5) ? "Disabled (comparison test only)" : "Enabled / Intel default", flag5 ? "13th/14th generation: retain Intel stability guidance, including enabled C-states. The comparison checkbox does not override this. Use the current stable BIOS recommended for your exact board." : "Disabling idle states increases idle power/heat and is not a guaranteed FPS gain. Keep only if repeatable tests improve; otherwise restore your original value.");
					s.AppendLine("Unlocked K/KF and non-K CPUs do not share multiplier controls. No universal ratios, current limits, E-core disabling or undervolting preset is supplied.");
				}
				s.AppendLine("\nMEMORY");
				Row(brand switch
				{
					"ASUS" => "Ai Tweaker / Extreme Tweaker --> Ai Overclock Tuner", 
					"MSI" => "OC --> " + (flag2 ? "EXPO / A-XMP" : (flag ? "A-XMP" : "Extreme Memory Profile (XMP)")), 
					"Gigabyte" => "Tweaker --> XMP / EXPO Profile", 
					_ => "OC Tweaker --> DRAM Profile Configuration (or DRAM Configuration) --> Load XMP / EXPO Setting", 
				}, (!memory) ? "Auto / JEDEC" : (flag2 ? "Kit-supported EXPO profile (XMP only if compatible)" : "Kit-supported XMP profile"), memory ? "Memory profiles are overclocking. Verify the kit/board/CPU combination; no fixed MHz, timings or voltage is universal. Test memory stability; restore Auto if unstable." : "Enable the memory-profile option above to include kit-specific overclocking. Leave manual voltage/timings alone.");
				s.AppendLine("\nDO NOT CHANGE AS A GENERIC TWEAK\nSecure Boot, TPM, storage/boot mode, thermal protections, virtualization and manual CPU/SoC voltage. Preserve these unless solving a specific, verified problem.");
				s.AppendLine("\nVALIDATE / REVERT\nCompare the same game scene/settings over repeated runs, including 1% lows. Check temperatures, idle/sleep stability and hardware errors. Restore original values if worse; use the exact manual's recovery instructions for a failed boot.");
				s.AppendLine("\nSOURCES / PATH EXAMPLES\n" + Reference(brand, flag));
				s.AppendLine("AMD PBO support/warranty: https://www.amd.com/en/products/software/ryzen-master.html");
				return s.ToString();
			}
		}
		s.AppendLine("\nNO TUNING PRESET SELECTED\nUnknown/OEM/mobile hardware: use the exact system manual. No generic overclocking prescription.");
		return s.ToString();
		void Row(string path, string value, string note)
		{
			s.AppendLine();
			s.AppendLine(path + " --> " + value);
			s.AppendLine(note);
		}
	}

	public static string Reference(string brand, bool amd)
	{
		switch (brand)
		{
		case "ASUS":
			if (amd)
			{
				return "https://dlcdnets.asus.com/pub/ASUS/mb/13MANUAL/PRIME_PROART_TUF_GAMING_AMD_AM5_Series_BIOS_EM_WEB_EN.pdf";
			}
			break;
		case "MSI":
			if (amd)
			{
				return "https://download.msi.com/archive/mnu_exe/mb/AMDAM5BIOS.pdf";
			}
			return "https://download.msi.com/manual/mb/Intel700BIOS.pdf";
		case "Gigabyte":
			if (amd)
			{
				return "https://download.gigabyte.com/FileList/Manual/mb_manual_amd600series-bios_e_0104.pdf";
			}
			return "https://download.gigabyte.com/FileList/Manual/mb_manual_intel700series-bios_e.pdf";
		case "ASRock":
			if (amd)
			{
				return "https://download.asrock.com/Manual/Software/AMD%20B650/Software_BIOS%20Setup%20Guide_English.pdf";
			}
			break;
		}
		return "Exact-model manual required: " + BiosViewModel.GetSupportUrl(brand);
	}
}
