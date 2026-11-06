using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using QuantixTweakingUtility.Models;

namespace QuantixTweakingUtility.Services;

public record AdapterOffloadState(Guid Id, string Name, string DriverVersion, bool IPv4, bool IPv6);
public interface IAdapterOffloadStore
{
    Task<Guid> ActiveId();
    Task<AdapterOffloadState> Read(Guid id, bool rsc);
    Task Write(Guid id, bool rsc, bool ipv4, bool ipv6);
}

public sealed class WindowsAdapterOffloadStore : IAdapterOffloadStore
{
    public async Task<Guid> ActiveId()
    {
        var result = await NetworkWorkflow.RunAsync("Inspect");
        if (!result.Success || result.Connection is not { Physical: true } connection || !Guid.TryParse(connection.Id, out var id))
            throw new IOException("An active physical network adapter is required.");
        return id;
    }

    public async Task<AdapterOffloadState> Read(Guid id, bool rsc)
    {
        string script = WindowsAdapterRssStore.Find(id) +
            ";$state=@($adapter | Get-NetAdapter" + (rsc ? "Rsc" : "Lso") +
            " -ErrorAction Stop);if($state.Count -ne 1){throw 'Offload settings are unavailable on this adapter.'};" +
            "[pscustomobject]@{Id=([guid]$adapter.InterfaceGuid).ToString('D');Name=[string]$adapter.Name;DriverVersion=[string]$adapter.DriverVersion;IPv4=[bool]$state[0].IPv4Enabled;IPv6=[bool]$state[0].IPv6Enabled}|ConvertTo-Json -Compress";
        return JsonSerializer.Deserialize<AdapterOffloadState>(await WindowsAdapterRssStore.Run(script))
            ?? throw new IOException("Offload status was unavailable.");
    }

    public async Task Write(Guid id, bool rsc, bool ipv4, bool ipv6)
    {
        // Use the exact CIM adapter object, never a wildcard or a persisted display name.
        await WindowsAdapterRssStore.Run(WindowsAdapterRssStore.Find(id) + ";$adapter | Set-NetAdapter" +
            (rsc ? "Rsc" : "Lso") + " -IPv4Enabled " + (ipv4 ? "$true" : "$false") +
            " -IPv6Enabled " + (ipv6 ? "$true" : "$false") + " -Confirm:$false -ErrorAction Stop");
    }
}

public static class AdapterOffloadTweaks
{
    public static TweakDefinition Build(IRestoreService restore, bool rsc, IAdapterOffloadStore? store = null)
    {
        store ??= new WindowsAdapterOffloadStore();
        var gate = new SemaphoreSlim(1, 1);
        string id = rsc ? "network.adapter.rsc.disable" : "network.adapter.lso.disable";
        string label = rsc ? "RSC" : "LSOv2";
        return new TweakDefinition
        {
            Id = id, Name = rsc ? "NIC Coalescing Off (RSC)" : "NIC Large Send Offload Off",
            Category = TweakCategory.Network, RiskLevel = TweakRiskLevel.High, RequiresAdministrator = true,
            Description = (rsc ? "Disables receive segment coalescing for IPv4 and IPv6" : "Disables large send offload v2 for IPv4 and IPv6") +
                " on the active physical network adapter when supported. The adapter may restart and briefly disconnect. This can increase CPU work and reduce throughput; lower latency is not guaranteed. Elite saves both protocol states and restores the original adapter even if another connection becomes active. Driver changes require review before restore." +
                (rsc ? "" : " Legacy LSOv1 remains unchanged."),
            AffectedSettings = new[] { "NetAdapter:active:" + label },
            HasSavedOriginal = () => restore.GetBackup(id) != null,
            CurrentStateReader = async () =>
            {
                try
                {
                    var state = await store.Read(await store.ActiveId(), rsc);
                    return !state.IPv4 && !state.IPv6 ? TweakState.Applied : TweakState.NotApplied;
                }
                catch (IOException) { return TweakState.Unsupported; }
            },
            ApplyAction = () => Change(true), RestoreAction = () => Change(false)
        };

        async Task<TweakActionResult> Change(bool apply)
        {
            await gate.WaitAsync();
            try
            {
                var raw = restore.GetBackup(id);
                var original = raw == null ? null : JsonSerializer.Deserialize<AdapterOffloadState>(raw)
                    ?? throw new IOException("Invalid saved offload settings.");
                if (original != null && (original.Id == Guid.Empty || string.IsNullOrWhiteSpace(original.DriverVersion)))
                    throw new IOException("Saved adapter identity is incomplete; the original has been retained.");
                if (!apply && original == null) return TweakActionResult.Fail("No original offload settings were saved by Elite.");
                Guid target = apply ? await store.ActiveId() : original!.Id;
                if (original != null && target != original.Id)
                    throw new IOException("The active adapter differs from the saved original. Restore the original adapter before applying to another connection.");
                var current = await store.Read(target, rsc);
                if (current.Id != target || string.IsNullOrWhiteSpace(current.DriverVersion))
                    throw new IOException("The adapter identity could not be verified.");
                if (original != null && current.DriverVersion != original.DriverVersion)
                    throw new IOException("The adapter driver changed. The original backup has been retained for review.");
                if (apply && original == null)
                {
                    raw = JsonSerializer.Serialize(current);
                    restore.SaveBackup(id, raw);
                    if (restore.GetBackup(id) != raw) throw new IOException("The original settings could not be saved and verified; no adapter change was requested.");
                    original = current;
                }
                bool ipv4 = !apply && original!.IPv4, ipv6 = !apply && original!.IPv6;
                if (current.IPv4 != ipv4 || current.IPv6 != ipv6) await store.Write(target, rsc, ipv4, ipv6);
                bool verified = false;
                for (int attempt = 0; attempt < 5; attempt++)
                {
                    try
                    {
                        var after = await store.Read(target, rsc);
                        verified = after.Id == target && after.DriverVersion == current.DriverVersion && after.IPv4 == ipv4 && after.IPv6 == ipv6;
                        if (verified) break;
                    }
                    catch (IOException) { }
                    if (attempt < 4) await Task.Delay(500);
                }
                if (!verified) throw new IOException("Offload verification did not complete. The original backup has been retained; reconnect the adapter and retry restore.");
                if (!apply) restore.ClearBackup(id);
                return TweakActionResult.Ok(label + (apply ? " disabled" : " restored") + " and verified on " + current.Name + ".");
            }
            finally { gate.Release(); }
        }
    }
}
