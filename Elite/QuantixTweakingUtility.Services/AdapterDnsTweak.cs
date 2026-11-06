using System;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using QuantixTweakingUtility.Models;

namespace QuantixTweakingUtility.Services;

public record AdapterDnsState(Guid Id, string Name, string[]? Servers);
public interface IAdapterDnsStore
{
    Task<AdapterDnsState> Read();
    Task Set(Guid id, string[]? servers);
}

public sealed class WindowsAdapterDnsStore : IAdapterDnsStore
{
    [StructLayout(LayoutKind.Sequential)]
    private struct DnsSettings
    {
        public uint Version;
        public ulong Flags;
        public IntPtr Domain, NameServer, SearchList;
        public uint RegistrationEnabled, RegisterAdapterName, EnableLlmnr, QueryAdapterName;
        public IntPtr ProfileNameServer;
    }
    [DllImport("iphlpapi.dll")] private static extern uint GetInterfaceDnsSettings(Guid id, ref DnsSettings settings);
    [DllImport("iphlpapi.dll")] private static extern void FreeInterfaceDnsSettings(ref DnsSettings settings);

    public async Task<AdapterDnsState> Read()
    {
        if (Environment.OSVersion.Version.Build < 19041) throw new IOException("DNS configuration discovery requires Windows 10 build 19041 or later.");
        var inspection = await NetworkWorkflow.RunAsync("Inspect");
        if (!inspection.Success || inspection.Connection is not { Physical: true } connection || !Guid.TryParse(connection.Id, out var id))
            throw new IOException("An active physical adapter is required.");
        var settings = new DnsSettings { Version = 1 };
        uint result = GetInterfaceDnsSettings(id, ref settings);
        if (result != 0) throw new IOException(new Win32Exception((int)result).Message);
        try
        {
            if ((settings.Flags & 1) != 0 || !string.IsNullOrWhiteSpace(Marshal.PtrToStringUni(settings.ProfileNameServer)))
                throw new IOException("This adapter has a DNS profile configuration that this IPv4 control cannot safely replace.");
            string? configured = Marshal.PtrToStringUni(settings.NameServer);
            var servers = string.IsNullOrWhiteSpace(configured) ? null : configured.Split(new[] { ' ', ',' }, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            ValidateServers(servers);
            return new(id, connection.Adapter, servers);
        }
        finally { FreeInterfaceDnsSettings(ref settings); }
    }
    public async Task Set(Guid id, string[]? servers)
    {
        ValidateServers(servers);
        string command = servers == null ? "-ResetServerAddresses" : "-ServerAddresses @(" + string.Join(",", servers.Select(server => "'" + IPAddress.Parse(server).ToString() + "'")) + ")";
        await WindowsAdapterRssStore.Run(WindowsAdapterRssStore.Find(id) + ";$dns=@(Get-DnsClientServerAddress -InterfaceIndex $adapter.ifIndex -AddressFamily IPv4 -ErrorAction Stop);if($dns.Count -ne 1){throw 'IPv4 DNS configuration unavailable.'};$dns | Set-DnsClientServerAddress " + command + " -Confirm:$false -ErrorAction Stop");
    }
    internal static void ValidateServers(string[]? servers)
    {
        if (servers == null) return;
        if (servers.Length is < 1 or > 16 || servers.Any(server => !IPAddress.TryParse(server, out var address) || address.AddressFamily != AddressFamily.InterNetwork))
            throw new IOException("Invalid saved IPv4 DNS server list.");
    }
}

public static class AdapterDnsTweak
{
    private static readonly string[] Cloudflare = { "1.1.1.1", "1.0.0.1" };
    public static TweakDefinition Build(IRestoreService restore, IAdapterDnsStore? store = null)
    {
        store ??= new WindowsAdapterDnsStore();
        var gate = new SemaphoreSlim(1, 1);
        const string id = "network.cloudflare-dns.ipv4";
        return new TweakDefinition
        {
            Id = id, Name = "Cloudflare DNS · IPv4",
            Description = "Sets the active adapter's IPv4 DNS servers to 1.1.1.1 and 1.0.0.1. Resolution performance depends on your connection; private or workplace names may stop resolving. Saves the original manual list or automatic DNS mode. IPv6 and DNS encryption settings remain unchanged.",
            Category = TweakCategory.Network, RiskLevel = TweakRiskLevel.High, RequiresAdministrator = true,
            AffectedSettings = new[] { "DNS:active:IPv4:servers" },
            CurrentStateReader = async () =>
            {
                try { return Same((await store.Read()).Servers, Cloudflare) ? TweakState.Applied : TweakState.NotApplied; }
                catch (IOException) { return TweakState.Unsupported; }
                catch (EntryPointNotFoundException) { return TweakState.Unsupported; }
            },
            ApplyAction = () => Change(true), RestoreAction = () => Change(false)
        };
        async Task<TweakActionResult> Change(bool apply)
        {
            await gate.WaitAsync();
            try
            {
                var current = await store.Read();
                var saved = restore.GetBackup(id);
                var original = saved == null ? null : JsonSerializer.Deserialize<AdapterDnsState>(saved) ?? throw new IOException("Invalid saved DNS configuration.");
                if (original != null)
                {
                    if (original.Id != current.Id) throw new IOException("The active adapter differs from the saved original. Reconnect using the original adapter before restoring DNS.");
                    WindowsAdapterDnsStore.ValidateServers(original.Servers);
                    if (!Same(current.Servers, original.Servers) && !Same(current.Servers, Cloudflare))
                        throw new IOException("DNS was changed outside Elite. The original backup has been retained.");
                }
                if (!apply && original == null) return TweakActionResult.Fail("No original DNS configuration was saved by Elite.");
                if (apply && original == null) restore.SaveBackup(id, JsonSerializer.Serialize(current));
                var target = apply ? Cloudflare : original!.Servers;
                if (!Same(current.Servers, target)) await store.Set(current.Id, target);
                var verified = await store.Read();
                if (verified.Id != current.Id || !Same(verified.Servers, target)) throw new IOException("DNS configuration readback did not match. The original backup has been retained.");
                if (!apply) restore.ClearBackup(id);
                return TweakActionResult.Ok(apply ? "Cloudflare IPv4 DNS configured and verified." : "Original IPv4 DNS configuration restored and verified.");
            }
            finally { gate.Release(); }
        }
    }
    private static bool Same(string[]? left, string[]? right) => left == null || right == null ? left == right
        : left.Select(value => IPAddress.Parse(value).ToString()).SequenceEqual(right.Select(value => IPAddress.Parse(value).ToString()), StringComparer.Ordinal);
}
