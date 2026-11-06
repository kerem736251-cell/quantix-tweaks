using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using QuantixTweakingUtility.Models;

namespace QuantixTweakingUtility.Services;

public enum WindowsRepairOperation { SystemFileChecker, RestoreHealth }
public record WindowsRepairResult(int ExitCode, string Output, string LogFile);
public interface IWindowsRepairRunner { Task<WindowsRepairResult> Run(WindowsRepairOperation operation); }

public sealed class WindowsRepairRunner : IWindowsRepairRunner
{
    public static ProcessStartInfo Command(WindowsRepairOperation operation)
    {
        string system = Environment.GetFolderPath(Environment.SpecialFolder.System);
        var command = new ProcessStartInfo(Path.Combine(system, operation switch
        {
            WindowsRepairOperation.SystemFileChecker => "sfc.exe",
            WindowsRepairOperation.RestoreHealth => "dism.exe",
            _ => throw new ArgumentOutOfRangeException(nameof(operation))
        }))
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = operation == WindowsRepairOperation.SystemFileChecker ? Encoding.Unicode : Encoding.UTF8
        };
        if (operation == WindowsRepairOperation.SystemFileChecker) command.ArgumentList.Add("/scannow");
        else foreach (string argument in new[] { "/Online", "/Cleanup-Image", "/RestoreHealth", "/NoRestart", "/English" }) command.ArgumentList.Add(argument);
        return command;
    }
    public async Task<WindowsRepairResult> Run(WindowsRepairOperation operation)
    {
        var command = Command(operation);
        string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Quantix", "logs", "repairs");
        Directory.CreateDirectory(folder);
        string logFile = Path.Combine(folder, operation + "-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff") + ".txt");
        // Confirm the output location before starting an operation that can take a long time.
        await File.WriteAllTextAsync(logFile, command.FileName + " " + string.Join(" ", command.ArgumentList) + Environment.NewLine);
        using var process = Process.Start(command) ?? throw new IOException("Windows repair could not be started.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        // Servicing must not be killed by an arbitrary short timeout.
        await process.WaitForExitAsync();
        string text = (await output).Replace("\0", "") + Environment.NewLine + (await error).Replace("\0", "");
        await File.AppendAllTextAsync(logFile, text + Environment.NewLine + "Exit code: " + process.ExitCode);
        return new(process.ExitCode, text.Trim(), logFile);
    }
}

public static class WindowsRepairActions
{
    // DISM and SFC should not run concurrently, including through separate cards.
    private static readonly SemaphoreSlim Gate = new(1, 1);
    public static TweakDefinition Build(WindowsRepairOperation operation, IWindowsRepairRunner? runner = null)
    {
        runner ??= new WindowsRepairRunner();
        bool sfc = operation == WindowsRepairOperation.SystemFileChecker;
        if (!sfc && operation != WindowsRepairOperation.RestoreHealth) throw new ArgumentOutOfRangeException(nameof(operation));
        return new TweakDefinition
        {
            Id = sfc ? "windows.repair.sfc" : "windows.repair.restore-health",
            Name = sfc ? "SFC /scannow" : "Restore Health", Category = TweakCategory.Windows,
            Description = sfc
                ? "Runs Windows System File Checker to verify and repair protected system files. This can take several minutes. Microsoft recommends running Restore Health first. The Windows result is saved to a log; repairs are a one-time action with no toggle to restore."
                : "Runs DISM /Online /Cleanup-Image /RestoreHealth to repair the Windows component store. Windows Update may download repair content. This can take several minutes; automatic restart is disabled. Run SFC afterward to check protected system files. Repairs cannot be undone with Restore.",
            RiskLevel = TweakRiskLevel.Low, RequiresAdministrator = true, IsOneTimeAction = true,
            AffectedSettings = new[] { sfc ? "action:windows:system-file-checker" : "action:windows:component-store-repair" },
            CurrentStateReader = () => Task.FromResult(TweakState.NotApplied),
            RestoreAction = () => Task.FromResult(TweakActionResult.Fail("Windows repair is a one-time operation; it has no saved setting to restore.")),
            ApplyAction = async () =>
            {
                await Gate.WaitAsync();
                try
                {
                    var result = await runner.Run(operation);
                    string details = string.IsNullOrWhiteSpace(result.Output) ? "Windows returned no console output." : result.Output;
                    string message = (sfc ? "SFC" : "DISM") + " finished with exit code " + result.ExitCode
                        + (result.ExitCode == 3010 && !sfc ? ". Restart Windows to finish servicing." : ". Review the Windows result below.")
                        + Environment.NewLine + details + Environment.NewLine + "Log: " + result.LogFile;
                    // Do not interpret an exit code as proof that all corruption was repaired.
                    return result.ExitCode == 0 || (!sfc && result.ExitCode == 3010) ? TweakActionResult.Ok(message) : TweakActionResult.Fail(message);
                }
                finally { Gate.Release(); }
            }
        };
    }
}
