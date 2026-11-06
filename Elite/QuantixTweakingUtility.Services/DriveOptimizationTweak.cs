using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using QuantixTweakingUtility.Models;

namespace QuantixTweakingUtility.Services;

public interface IDriveOptimizer
{
    bool Available { get; }
    Task<string> OptimizeSystemVolume();
}
public sealed class WindowsDriveOptimizer : IDriveOptimizer
{
    private readonly ILoggingService? log;
    private static string Executable => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "defrag.exe");
    public WindowsDriveOptimizer(ILoggingService? log = null) { this.log = log; }
    public bool Available => File.Exists(Executable);
    public static ProcessStartInfo Command(string volume, string executable)
    {
        // A drive-letter root only: no switches, UNC paths, wildcard volumes or shell interpretation.
        if (volume.Length != 3 || !char.IsAsciiLetter(volume[0]) || volume[1] != ':' || volume[2] != '\\')
            throw new IOException("A local drive-letter root is required for optimization.");
        var command = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden, RedirectStandardOutput = true, RedirectStandardError = true };
        command.ArgumentList.Add(volume[..2]);
        command.ArgumentList.Add("/O"); // Let Windows choose the correct operation for the media.
        command.ArgumentList.Add("/U"); command.ArgumentList.Add("/V");
        return command;
    }
    public async Task<string> OptimizeSystemVolume()
    {
        if (!Available) throw new IOException("Windows Drive Optimizer is not installed.");
        string root = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows))
            ?? throw new IOException("The Windows volume could not be identified.");
        var drive = new DriveInfo(root);
        if (!drive.IsReady || drive.DriveType != DriveType.Fixed) throw new IOException("The Windows volume must be a ready local fixed drive.");
        using var process = Process.Start(Command(root, Executable)) ?? throw new IOException("Windows Drive Optimizer could not start.");
        var output = process.StandardOutput.ReadToEndAsync(); var errors = process.StandardError.ReadToEndAsync();
        // Optimization can take considerably longer than a normal settings write. Wait for the actual result.
        await process.WaitForExitAsync();
        string report = (await output).Trim(), error = (await errors).Trim();
        log?.Info("DriveOptimizer", "Volume " + root + "; exit " + process.ExitCode + ". " + report + " " + error);
        if (process.ExitCode != 0) throw new IOException("Windows Drive Optimizer returned exit code " + process.ExitCode + ". " + error + " " + report);
        return root;
    }
}
public static class DriveOptimizationTweak
{
    public static TweakDefinition Build(IDriveOptimizer? optimizer = null)
    {
        optimizer ??= new WindowsDriveOptimizer(); var gate = new SemaphoreSlim(1, 1);
        return new TweakDefinition
        {
            Id = "storage.optimize-system-drive", Name = "Optimize Drive", Category = TweakCategory.Storage,
            Description = "Runs Windows Drive Optimizer on the Windows system volume using media-aware optimization (/O). Windows chooses the appropriate operation for the drive rather than forcing HDD defragmentation on every disk. It may take several minutes and create disk activity; Elite waits for Windows to finish. Other volumes and the scheduled optimization policy remain unchanged. This is a one-time maintenance action with no setting to restore.",
            RiskLevel = TweakRiskLevel.Low, RequiresAdministrator = true, IsOneTimeAction = true,
            AffectedSettings = new[] { "action:storage:optimize-system-volume" },
            CurrentStateReader = () => Task.FromResult(optimizer.Available ? TweakState.NotApplied : TweakState.Unsupported),
            RestoreAction = () => Task.FromResult(TweakActionResult.Fail("Drive optimization is a one-time maintenance operation; there is no saved setting to restore.")),
            ApplyAction = async () =>
            {
                await gate.WaitAsync();
                try
                {
                    if (!optimizer.Available) return TweakActionResult.Fail("Windows Drive Optimizer is not installed.");
                    string volume = await optimizer.OptimizeSystemVolume();
                    return TweakActionResult.Ok("Windows Drive Optimizer completed media-aware optimization on " + volume + ".");
                }
                finally { gate.Release(); }
            }
        };
    }
}
