using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace QuantixTweakingUtility.Services;

public sealed class WindowsMaintenanceService : IWindowsMaintenanceService
{
	private readonly ILoggingService _logging;

	public WindowsMaintenanceService(ILoggingService logging)
	{
		_logging = logging;
	}

	public async Task<(bool Success, string Message)> VerifySystemFilesAsync()
	{
		_ = 2;
		try
		{
			ProcessStartInfo startInfo = new ProcessStartInfo
			{
				FileName = "sfc",
				Arguments = "/verifyonly",
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				UseShellExecute = false,
				CreateNoWindow = true
			};
			using Process process = Process.Start(startInfo);
			if (process == null)
			{
				return (Success: false, Message: "Windows could not start System File Checker.");
			}
			Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
			Task<string> errorTask = process.StandardError.ReadToEndAsync();
			await process.WaitForExitAsync();
			string output = await outputTask;
			string text = await errorTask;
			if (process.ExitCode == 0)
			{
				_logging.Info("WindowsMaintenance", "Completed SFC verification.");
				return (Success: true, Message: output.Contains("did not find", StringComparison.OrdinalIgnoreCase) ? "System file verification completed: no integrity violations found." : "System file verification completed. Review Windows logs for full details.");
			}
			_logging.Warn("WindowsMaintenance", "SFC verification ended with code " + process.ExitCode);
			string text2 = (string.IsNullOrWhiteSpace(text) ? output : text);
			return (Success: false, Message: string.IsNullOrWhiteSpace(text2) ? "System file verification could not complete." : text2.Trim());
		}
		catch (Exception ex)
		{
			_logging.Error("WindowsMaintenance", "SFC verification failed.", ex);
			return (Success: false, Message: "System file verification could not start. It may require administrator rights.");
		}
	}
}
