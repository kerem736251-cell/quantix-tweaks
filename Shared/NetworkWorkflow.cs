using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace QuantixTweakingUtility.Services;

public sealed record ConnectionDetails(string Type, string Status, string Adapter, string Id, int Index, string LinkSpeed, string Dns, bool Physical);
public sealed record NetworkWorkflowResult(bool Success, string Message, ConnectionDetails? Connection = null);

public static class NetworkWorkflow
{
#if QUANTIX_ELITE
    public const string Tier = "Elite";
#else
    public const string Tier = "Pro";
#endif
    public static async Task<NetworkWorkflowResult> RunAsync(string mode)
    {
        if (mode != "Inspect" && mode != "Optimize" && mode != "Restore" && mode != "EthernetMinimal")
            throw new ArgumentException("Unknown network operation.", nameof(mode));
        try
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Quantix.Network.ps1")
                ?? throw new IOException("Network workflow resource missing.");
            using var reader = new StreamReader(stream);
            string script = "& {\n" + await reader.ReadToEndAsync() + "\n} -Mode " + mode + " -Tier " + Tier;
            var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
                "WindowsPowerShell", "v1.0", "powershell.exe"))
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true,
                StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
            };
            start.ArgumentList.Add("-NoProfile"); start.ArgumentList.Add("-NonInteractive");
            start.ArgumentList.Add("-EncodedCommand");
            start.ArgumentList.Add(Convert.ToBase64String(Encoding.Unicode.GetBytes(
                "[Console]::OutputEncoding=[Text.UTF8Encoding]::new($false); Invoke-Expression ([Console]::In.ReadToEnd())")));
            using var process = Process.Start(start) ?? throw new IOException("Windows PowerShell unavailable.");
            var output = process.StandardOutput.ReadToEndAsync();
            var errors = process.StandardError.ReadToEndAsync();
            await process.StandardInput.WriteAsync(script);
            process.StandardInput.Close();
            // Let writes finish: terminating mid-change could leave only a partial restore.
            await process.WaitForExitAsync();
            string text = await output;
            if (process.ExitCode != 0) return new(false, "Network operation failed: " + await errors);
            return JsonSerializer.Deserialize<NetworkWorkflowResult>(text,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? new(false, "Windows returned no network information.");
        }
        catch (Exception ex) { return new(false, ex.Message); }
    }
}
