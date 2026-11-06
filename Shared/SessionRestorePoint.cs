using System;
using System.Linq;
using System.Management;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace QuantixTweakingUtility.Services;

public static class SessionRestorePoint
{
    private static int started;
    public static string Status { get; private set; } = "Restore point not requested.";
    public static Task? Completion { get; private set; }

    public static void StartOnce(Action<string>? log = null, Func<string>? create = null)
    {
        if (Interlocked.Exchange(ref started, 1) != 0) return;
        Status = "Creating startup restore point in the background...";
        Completion = Task.Run(() =>
        {
            try { Status = (create ?? Create)(); }
            catch (Exception ex) { Status = "Restore point unavailable: " + ex.Message; }
            try { log?.Invoke(Status); } catch { /* Logging must not block startup. */ }
        });
    }

    private static string Create()
    {
        if (!AdminHelper.IsRunningAsAdministrator())
            throw new UnauthorizedAccessException("Administrator rights are required. The app remains available.");
        using var stream = typeof(SessionRestorePoint).Assembly.GetManifestResourceStream("Quantix.RestorePoint.ps1")
            ?? throw new System.IO.IOException("Restore-point helper missing.");
        using var reader = new System.IO.StreamReader(stream);
        string script = "[Console]::OutputEncoding=[Text.UTF8Encoding]::new($false); & {\n" + reader.ReadToEnd() + "\n} -Tier " + NetworkWorkflow.Tier;
        var start = new System.Diagnostics.ProcessStartInfo(System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe")) {
            UseShellExecute=false, CreateNoWindow=true, RedirectStandardOutput=true, RedirectStandardError=true,
            StandardOutputEncoding=System.Text.Encoding.UTF8, StandardErrorEncoding=System.Text.Encoding.UTF8
        };
        start.ArgumentList.Add("-NoProfile"); start.ArgumentList.Add("-NonInteractive"); start.ArgumentList.Add("-EncodedCommand");
        start.ArgumentList.Add(Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(script)));
        using var process = System.Diagnostics.Process.Start(start) ?? throw new System.IO.IOException("Windows PowerShell unavailable.");
        var errors = process.StandardError.ReadToEndAsync();
        string output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0) throw new InvalidOperationException(errors.GetAwaiter().GetResult());
        using var json = System.Text.Json.JsonDocument.Parse(output);
        return json.RootElement.GetProperty("Message").GetString() ?? "Restore-point status unavailable.";
    }
}
