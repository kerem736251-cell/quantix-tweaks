using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using QuantixTweakingUtility.Models;

namespace QuantixTweakingUtility.Services;

public record AdapterRssState(Guid Id, string Name, bool Enabled);
public interface IAdapterRssStore
{
    Task<AdapterRssState> Active();
    Task Set(Guid id, bool enabled);
}

public sealed class WindowsAdapterRssStore : IAdapterRssStore
{
    public async Task<AdapterRssState> Active()
    {
        var inspection = await NetworkWorkflow.RunAsync("Inspect");
        if (!inspection.Success || inspection.Connection is not { Physical: true } connection || !Guid.TryParse(connection.Id, out var id))
            throw new IOException("An active physical network adapter is required for RSS.");
        var script = Find(id) + ";$rss=@($adapter | Get-NetAdapterRss -ErrorAction Stop);if($rss.Count -ne 1){throw 'RSS is not supported on this adapter.'};[pscustomobject]@{Id=([guid]$adapter.InterfaceGuid).ToString('D');Name=[string]$adapter.Name;Enabled=[bool]$rss[0].Enabled}|ConvertTo-Json -Compress";
        return JsonSerializer.Deserialize<AdapterRssState>(await Run(script)) ?? throw new IOException("RSS status was unavailable.");
    }
    public async Task Set(Guid id, bool enabled)
    {
        await Run(Find(id) + ";$adapter | " + (enabled ? "Enable-NetAdapterRss" : "Disable-NetAdapterRss") + " -Confirm:$false -ErrorAction Stop");
    }
    internal static string Find(Guid id) => "$adapter=@(Get-NetAdapter -ErrorAction Stop | Where-Object {([guid]$_.InterfaceGuid) -eq [guid]'" + id.ToString("D") + "'});if($adapter.Count -ne 1 -or !$adapter[0].HardwareInterface){throw 'Original physical adapter unavailable.'};$adapter=$adapter[0]";
    internal static async Task<string> Run(string script)
    {
        var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "WindowsPowerShell", "v1.0", "powershell.exe"))
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true,
            RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8
        };
        start.ArgumentList.Add("-NoProfile"); start.ArgumentList.Add("-NonInteractive");
        start.ArgumentList.Add("-EncodedCommand");
        var wrapped = "$ErrorActionPreference='Stop';$ProgressPreference='SilentlyContinue';[Console]::OutputEncoding=[Text.UTF8Encoding]::new($false);try{" + script + "}catch{[Console]::Error.WriteLine($_.Exception.Message);exit 1}";
        start.ArgumentList.Add(Convert.ToBase64String(Encoding.Unicode.GetBytes(wrapped)));
        using var process = Process.Start(start) ?? throw new IOException("Cannot start Windows adapter tools.");
        var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) { process.Kill(true); await process.WaitForExitAsync(); await Task.WhenAll(output, error); throw new IOException("Adapter operation timed out. Any saved original has been retained."); }
        if (process.ExitCode != 0) throw new IOException((await error).Trim());
        return await output;
    }
}

public static class AdapterRssTweak
{
    public static TweakDefinition Build(IRestoreService restore, IAdapterRssStore? store = null)
    {
        store ??= new WindowsAdapterRssStore();
        var gate = new SemaphoreSlim(1, 1);
        const string id = "network.adapter-rss.enable";
        return new TweakDefinition
        {
            Id = id, Name = "Enable Network Adapter RSS",
            Description = "Enables receive-side scaling on the active physical adapter when the driver supports it. RSS distributes receive processing across CPU cores. The adapter may restart and briefly interrupt connections. Elite saves the original state for this adapter; processor affinity and queue counts remain unchanged.",
            Category = TweakCategory.Network, RiskLevel = TweakRiskLevel.High, RequiresAdministrator = true,
            AffectedSettings = new[] { "NetAdapter:active:ReceiveSideScaling" },
            CurrentStateReader = async () =>
            {
                try { return (await store.Active()).Enabled ? TweakState.Applied : TweakState.NotApplied; }
                catch (IOException) { return TweakState.Unsupported; }
            },
            ApplyAction = () => Change(true), RestoreAction = () => Change(false)
        };
        async Task<TweakActionResult> Change(bool apply)
        {
            await gate.WaitAsync();
            try
            {
                var current = await store.Active();
                var saved = restore.GetBackup(id);
                var original = saved == null ? null : JsonSerializer.Deserialize<AdapterRssState>(saved)
                    ?? throw new IOException("Invalid saved RSS state.");
                if (original != null && original.Id != current.Id)
                    throw new IOException("The active adapter differs from the saved original. Reconnect using the original adapter before restoring or reapplying RSS.");
                if (!apply && original == null) return TweakActionResult.Fail("No original RSS state was saved by Elite.");
                if (apply && original == null) restore.SaveBackup(id, JsonSerializer.Serialize(current));
                bool target = apply || original!.Enabled;
                if (current.Enabled != target) await store.Set(current.Id, target);
                // Verify through the adapter provider after its possible restart.
                AdapterRssState? after = null;
                for (int attempt = 0; attempt < 5; attempt++)
                {
                    try { after = await store.Active(); if (after.Id == current.Id && after.Enabled == target) break; }
                    catch (IOException) { }
                    if (attempt < 4) await Task.Delay(500);
                }
                if (after == null || after.Id != current.Id || after.Enabled != target)
                    throw new IOException("RSS verification did not complete after the adapter restart. The original backup has been retained.");
                if (!apply) restore.ClearBackup(id);
                return TweakActionResult.Ok("RSS " + (apply ? "enabled" : "restored") + " and verified on " + current.Name + ".");
            }
            finally { gate.Release(); }
        }
    }
}

