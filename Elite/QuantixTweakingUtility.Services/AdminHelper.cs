using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;

namespace QuantixTweakingUtility.Services;

public static class AdminHelper
{
	public static bool IsRunningAsAdministrator()
	{
		using WindowsIdentity ntIdentity = WindowsIdentity.GetCurrent();
		return new WindowsPrincipal(ntIdentity).IsInRole(WindowsBuiltInRole.Administrator);
	}

	public static bool TryRelaunchAsAdministrator(IEnumerable<string> arguments, out string? errorMessage)
	{
		errorMessage = null;
		string processPath = Environment.ProcessPath;
		if (string.IsNullOrWhiteSpace(processPath))
		{
			errorMessage = "Quantix could not locate its executable.";
			return false;
		}
		try
		{
			ProcessStartInfo processStartInfo = new ProcessStartInfo
			{
				FileName = processPath,
				UseShellExecute = true,
				Verb = "runas",
				WorkingDirectory = AppContext.BaseDirectory
			};
			foreach (string argument in arguments)
			{
				processStartInfo.ArgumentList.Add(argument);
			}
			return Process.Start(processStartInfo) != null;
		}
		catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
		{
			errorMessage = "Administrator access was cancelled. Quantix needs administrator access to manage system tweaks.";
			return false;
		}
		catch (Exception ex2)
		{
			errorMessage = "Quantix could not request administrator access: " + ex2.Message;
			return false;
		}
	}
}
