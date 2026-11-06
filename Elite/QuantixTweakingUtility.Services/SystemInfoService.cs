using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Threading.Tasks;
using Microsoft.Win32;
using QuantixTweakingUtility.Models;

namespace QuantixTweakingUtility.Services;

public sealed class SystemInfoService : ISystemInfoService
{
	private readonly ILoggingService _logging;

	private SystemSnapshot? _staticInfoCache;

	public SystemInfoService(ILoggingService logging)
	{
		_logging = logging;
	}

	public async Task<SystemSnapshot> GetStaticInfoAsync()
	{
		if (_staticInfoCache != null)
		{
			return _staticInfoCache;
		}
		_staticInfoCache = await Task.Run((Func<SystemSnapshot>)DetectHardware);
		return _staticInfoCache;
	}

	private SystemSnapshot DetectHardware()
	{
		return new SystemSnapshot
		{
			CpuName = ReadCpuName(),
			GpuName = ReadGpuNames(),
			RamTotalDisplay = ReadMemoryDescription(),
			OsDisplay = GetFriendlyOsVersion(),
			Uptime = TimeSpan.FromMilliseconds(Environment.TickCount64)
		};
	}

	private string ReadCpuName()
	{
		try
		{
			using ManagementObjectSearcher managementObjectSearcher = new ManagementObjectSearcher("SELECT Name FROM Win32_Processor");
			foreach (ManagementBaseObject item in managementObjectSearcher.Get())
			{
				string text = item["Name"]?.ToString()?.Trim();
				if (!string.IsNullOrWhiteSpace(text))
				{
					return text;
				}
			}
		}
		catch (Exception ex)
		{
			_logging.Error("SystemInfoService", "Failed reading CPU identity via WMI.", ex);
		}
		return "Unknown CPU";
	}

	private string ReadGpuNames()
	{
		try
		{
			using ManagementObjectSearcher managementObjectSearcher = new ManagementObjectSearcher("SELECT Name, AdapterCompatibility FROM Win32_VideoController");
			List<(string, string)> list = new List<(string, string)>();
			foreach (ManagementBaseObject item in managementObjectSearcher.Get())
			{
				string text = item["Name"]?.ToString()?.Trim();
				if (!string.IsNullOrWhiteSpace(text))
				{
					list.Add((text, item["AdapterCompatibility"]?.ToString()?.Trim() ?? string.Empty));
				}
			}
			List<(string, string)> list2 = (from adapter in list
				where !adapter.Item1.Contains("Remote Display", StringComparison.OrdinalIgnoreCase)
				where !adapter.Item1.Contains("Basic Display", StringComparison.OrdinalIgnoreCase)
				select adapter).ToList();
			List<string> list3 = ((list2.Count > 0) ? list2 : list).Select<(string, string), string>(((string Name, string Vendor) adapter) => (string.IsNullOrWhiteSpace(adapter.Vendor) || adapter.Name.Contains(adapter.Vendor, StringComparison.OrdinalIgnoreCase)) ? adapter.Name : (adapter.Name + " · " + adapter.Vendor)).Distinct<string>(StringComparer.OrdinalIgnoreCase).ToList();
			return (list3.Count == 0) ? "Unknown GPU" : string.Join(Environment.NewLine, list3);
		}
		catch (Exception ex)
		{
			_logging.Error("SystemInfoService", "Failed reading GPU identity via WMI.", ex);
			return "Unknown GPU";
		}
	}

	private string ReadMemoryDescription()
	{
		try
		{
			using ManagementObjectSearcher managementObjectSearcher = new ManagementObjectSearcher("SELECT Capacity, ConfiguredClockSpeed, Speed, SMBIOSMemoryType FROM Win32_PhysicalMemory");
			ulong num = 0uL;
			List<uint> list = new List<uint>();
			List<ushort> list2 = new List<ushort>();
			foreach (ManagementBaseObject item in managementObjectSearcher.Get())
			{
				if (ulong.TryParse(item["Capacity"]?.ToString(), out var result))
				{
					num += result;
				}
				uint result3;
				if (uint.TryParse(item["ConfiguredClockSpeed"]?.ToString(), out var result2) && result2 != 0)
				{
					list.Add(result2);
				}
				else if (uint.TryParse(item["Speed"]?.ToString(), out result3) && result3 != 0)
				{
					list.Add(result3);
				}
				if (ushort.TryParse(item["SMBIOSMemoryType"]?.ToString(), out var result4) && result4 > 0)
				{
					list2.Add(result4);
				}
			}
			if (num == 0L)
			{
				return ReadFallbackMemoryTotal();
			}
			List<string> list3 = new List<string> { $"{(double)num / 1024.0 / 1024.0 / 1024.0:0.#} GB" };
			string text = list2.Select(GetMemoryTypeName).FirstOrDefault((string name) => name != null);
			if (text != null)
			{
				list3.Add(text);
			}
			if (list.Count > 0)
			{
				list3.Add($"{list.Min()} MT/s");
			}
			return string.Join(" • ", list3);
		}
		catch (Exception ex)
		{
			_logging.Error("SystemInfoService", "Failed reading installed memory via WMI.", ex);
			return ReadFallbackMemoryTotal();
		}
	}

	private string ReadFallbackMemoryTotal()
	{
		try
		{
			using ManagementObjectSearcher managementObjectSearcher = new ManagementObjectSearcher("SELECT TotalPhysicalMemory FROM Win32_ComputerSystem");
			foreach (ManagementBaseObject item in managementObjectSearcher.Get())
			{
				if (double.TryParse(item["TotalPhysicalMemory"]?.ToString(), out var result))
				{
					return $"{result / 1024.0 / 1024.0 / 1024.0:0.#} GB";
				}
			}
		}
		catch (Exception ex)
		{
			_logging.Error("SystemInfoService", "Failed reading fallback memory total via WMI.", ex);
		}
		return "Unknown RAM";
	}

	private static string? GetMemoryTypeName(ushort smbiosType)
	{
		return smbiosType switch
		{
			20 => "DDR", 
			21 => "DDR2", 
			24 => "DDR3", 
			26 => "DDR4", 
			30 => "LPDDR4", 
			34 => "DDR5", 
			35 => "LPDDR5", 
			_ => null, 
		};
	}

	private static string GetFriendlyOsVersion()
	{
		try
		{
			using RegistryKey registryKey = Registry.LocalMachine.OpenSubKey("SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion");
			string text = registryKey?.GetValue("ProductName") as string;
			string text2 = registryKey?.GetValue("DisplayVersion") as string;
			if (!string.IsNullOrWhiteSpace(text))
			{
				return string.IsNullOrWhiteSpace(text2) ? text : (text + " " + text2);
			}
		}
		catch
		{
		}
		return Environment.OSVersion.VersionString;
	}
}
