using System;
using Microsoft.Win32;

namespace QuantixTweakingUtility.Services;

internal static class RegistryHelper
{
	public static int? ReadDword(RegistryKey hive, string subKeyPath, string valueName)
	{
		using RegistryKey registryKey = hive.OpenSubKey(subKeyPath, writable: false);
		return (registryKey?.GetValue(valueName) is int value) ? new int?(value) : ((int?)null);
	}

	public static void WriteDword(RegistryKey hive, string subKeyPath, string valueName, int value)
	{
		using RegistryKey registryKey = hive.CreateSubKey(subKeyPath, writable: true) ?? throw new InvalidOperationException("Could not open or create registry key: " + subKeyPath);
		registryKey.SetValue(valueName, value, RegistryValueKind.DWord);
	}

	public static string? ReadString(RegistryKey hive, string subKeyPath, string valueName)
	{
		using RegistryKey registryKey = hive.OpenSubKey(subKeyPath, writable: false);
		return registryKey?.GetValue(valueName) as string;
	}

	public static void WriteString(RegistryKey hive, string subKeyPath, string valueName, string value)
	{
		using RegistryKey registryKey = hive.CreateSubKey(subKeyPath, writable: true) ?? throw new InvalidOperationException("Could not open or create registry key: " + subKeyPath);
		registryKey.SetValue(valueName, value, RegistryValueKind.String);
	}

	public static void DeleteValue(RegistryKey hive, string subKeyPath, string valueName)
	{
		using RegistryKey registryKey = hive.OpenSubKey(subKeyPath, writable: true);
		registryKey?.DeleteValue(valueName, throwOnMissingValue: false);
	}
}
