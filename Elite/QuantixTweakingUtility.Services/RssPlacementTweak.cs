using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using QuantixTweakingUtility.Models;

namespace QuantixTweakingUtility.Services;

public record RssProcessor(int Group, int Number);
public record RssPlacement(string Profile, int BaseGroup, int BaseNumber, int MaxGroup, int MaxNumber, int MaxProcessors);
public record RssPlacementState(Guid Id, string Driver, bool Enabled, RssPlacement Placement, RssProcessor[] Processors);
public record RssPlacementBackup(RssPlacementState Original, RssPlacement Target);
public interface IRssPlacementStore
{
    Task<Guid> ActiveId();
    Task<RssPlacementState> Read(Guid id);
    Task Write(RssPlacementState expected, RssPlacement target);
}

public sealed class WindowsRssPlacementStore : IRssPlacementStore
{
    public Task<Guid> ActiveId() => new WindowsAdapterOffloadStore().ActiveId();
    // Missing provider data must stay missing: a PowerShell cast of null to int would invent zero.
    private const string ReadRss = ";$rss=@($adapter | Get-NetAdapterRss -ErrorAction Stop);if($rss.Count -ne 1){throw 'RSS configuration unavailable.'};$rss=$rss[0];" +
        "foreach($key in @('Profile','BaseProcessorGroup','BaseProcessorNumber','MaxProcessorGroup','MaxProcessorNumber','MaxProcessors','Enabled')){if($null -eq $rss.$key){throw ('Driver does not report RSS '+$key+'.')}};" +
        "$enumType=(Get-Command Set-NetAdapterRss).Parameters['Profile'].ParameterType;$profile=[Enum]::GetName($enumType,[Enum]::ToObject($enumType,[int]$rss.Profile));if(!$profile){throw 'Unknown RSS profile.'};";
    public async Task<RssPlacementState> Read(Guid id)
    {
        var json = await WindowsAdapterRssStore.Run(WindowsAdapterRssStore.Find(id) + ReadRss +
            "[pscustomobject]@{Id=([guid]$adapter.InterfaceGuid).ToString('D');Driver=[string]$adapter.DriverVersion;Enabled=[bool]$rss.Enabled;" +
            "Placement=[pscustomobject]@{Profile=$profile;BaseGroup=[int]$rss.BaseProcessorGroup;BaseNumber=[int]$rss.BaseProcessorNumber;MaxGroup=[int]$rss.MaxProcessorGroup;MaxNumber=[int]$rss.MaxProcessorNumber;MaxProcessors=[int]$rss.MaxProcessors};" +
            "Processors=@($rss.RssProcessorArray | Where-Object {$null -ne $_.ProcessorGroup -and $null -ne $_.ProcessorNumber} | ForEach-Object {[pscustomobject]@{Group=[int]$_.ProcessorGroup;Number=[int]$_.ProcessorNumber}})}|ConvertTo-Json -Depth 5 -Compress");
        return JsonSerializer.Deserialize<RssPlacementState>(json) ?? throw new IOException("RSS placement unavailable.");
    }
    public async Task Write(RssPlacementState expected, RssPlacement target)
    {
        RssPlacementTweak.Validate(expected);
        RssPlacementTweak.ValidatePlacement(target);
        var p = expected.Placement;
        // Check identity and all fields we change in the same PowerShell process as Set-NetAdapterRss.
        string guard = $"if([string]$adapter.DriverVersion -ne '{expected.Driver.Replace("'", "''")}' -or [bool]$rss.Enabled -ne ${expected.Enabled.ToString().ToLowerInvariant()} -or $profile -ne '{p.Profile}' -or $rss.BaseProcessorGroup -ne {p.BaseGroup} -or $rss.BaseProcessorNumber -ne {p.BaseNumber} -or $rss.MaxProcessorGroup -ne {p.MaxGroup} -or $rss.MaxProcessorNumber -ne {p.MaxNumber} -or $rss.MaxProcessors -ne {p.MaxProcessors}){{throw 'RSS configuration changed before write.'}};";
        await WindowsAdapterRssStore.Run(WindowsAdapterRssStore.Find(expected.Id) + ReadRss + guard +
            $"$adapter | Set-NetAdapterRss -Profile {target.Profile} -BaseProcessorGroup {target.BaseGroup} -BaseProcessorNumber {target.BaseNumber} -MaxProcessorGroup {target.MaxGroup} -MaxProcessorNumber {target.MaxNumber} -MaxProcessors {target.MaxProcessors} -Confirm:$false -ErrorAction Stop");
    }
}

public static class RssPlacementTweak
{
    private static readonly string[] Profiles = { "Closest", "ClosestStatic", "NUMA", "NUMAStatic", "Conservative" };
    internal static void ValidatePlacement(RssPlacement p)
    {
        if (p == null || !Profiles.Contains(p.Profile) || p.BaseGroup is < 0 or > 65535 || p.MaxGroup is < 0 or > 65535 ||
            p.BaseNumber is < 0 or > 63 || p.MaxNumber is < 0 or > 63 || p.MaxProcessors is < 1 or > 4096 ||
            p.BaseGroup > p.MaxGroup || (p.BaseGroup == p.MaxGroup && p.BaseNumber > p.MaxNumber))
            throw new IOException("RSS processor configuration is incomplete or invalid.");
    }
    internal static void Validate(RssPlacementState s)
    {
        if (s == null || s.Id == Guid.Empty || string.IsNullOrWhiteSpace(s.Driver)) throw new IOException("RSS adapter identity is incomplete.");
        ValidatePlacement(s.Placement);
    }
    private static RssPlacement Target(RssPlacementState s)
    {
        Validate(s);
        if (!s.Enabled) throw new IOException("Enable adapter RSS before applying core pinning.");
        if (s.Processors?.Any(p => p == null) == true) throw new IOException("Invalid RSS processor array.");
        var processors = (s.Processors ?? Array.Empty<RssProcessor>()).Distinct().OrderBy(p => p.Group).ThenBy(p => p.Number).ToArray();
        if (processors.Any(p => p.Group is < 0 or > 65535 || p.Number is < 0 or > 63)) throw new IOException("Invalid RSS processor array.");
        if (processors.Length < 2) throw new IOException("The driver must report at least two eligible RSS processors.");
        var first = processors[0];
        var second = processors.Skip(1).FirstOrDefault(p => p.Group == first.Group) ?? throw new IOException("Two eligible RSS processors in one group are required.");
        return new RssPlacement("ClosestStatic", first.Group, first.Number, second.Group, second.Number, 2);
    }
    public static TweakDefinition Build(IRestoreService restore, IRssPlacementStore? store = null)
    {
        store ??= new WindowsRssPlacementStore();
        const string id = "network.rss-core-pinning";
        var gate = new SemaphoreSlim(1, 1);
        return new TweakDefinition
        {
            Id = id, Name = "RSS Core Pinning", Category = TweakCategory.Network,
            RiskLevel = TweakRiskLevel.High, RequiresAdministrator = true,
            Description = "Restricts receive-side scaling to the first two driver-reported eligible processors in one group using ClosestStatic. This is a documented equivalent of the reference card; it does not promise physical cores 0/1 or lower ping. Requires enabled RSS and complete driver data. May restart the adapter and disconnect you; throughput may fall. Saves the original profile and processor limits for exact restore. Queue count and RSS enabled state remain unchanged.",
            AffectedSettings = new[] { "NetAdapter:active:RSSProcessorPlacement" },
            HasSavedOriginal = () => restore.GetBackup(id) != null,
            CurrentStateReader = async () =>
            {
                try { var s = await store.Read(await store.ActiveId()); return s.Placement == Target(s) ? TweakState.Applied : TweakState.NotApplied; }
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
                RssPlacementBackup? saved;
                try { saved = raw == null ? null : JsonSerializer.Deserialize<RssPlacementBackup>(raw); }
                catch (JsonException ex) { throw new IOException("Saved RSS configuration is invalid; retained for review.", ex); }
                if (raw != null && saved == null) throw new IOException("Saved RSS configuration is invalid.");
                if (saved != null) { Validate(saved.Original); ValidatePlacement(saved.Target); if (saved.Target != Target(saved.Original)) throw new IOException("Saved RSS target is inconsistent; retained for review."); }
                if (!apply && saved == null) return TweakActionResult.Fail("No original RSS placement was saved.");
                var adapter = apply ? await store.ActiveId() : saved!.Original.Id;
                if (saved != null && adapter != saved.Original.Id) throw new IOException("Restore the original adapter before applying to another connection.");
                var current = await store.Read(adapter); Validate(current);
                if (current.Id != adapter || (saved != null && current.Driver != saved.Original.Driver)) throw new IOException("RSS adapter identity or driver changed; backup retained.");
                if (apply && saved == null)
                {
                    saved = new RssPlacementBackup(current, Target(current));
                    raw = JsonSerializer.Serialize(saved); restore.SaveBackup(id, raw);
                    if (restore.GetBackup(id) != raw) throw new IOException("Cannot verify saved RSS original; no change requested.");
                }
                if (!Compatible(current.Placement, saved!.Original.Placement, saved.Target))
                    throw new IOException("RSS limits were changed outside Elite; backup retained for review.");
                var target = apply ? saved.Target : saved.Original.Placement;
                if (apply && !current.Enabled) throw new IOException("RSS is disabled; enable it before core pinning.");
                if (current.Placement != target) await store.Write(current, target);
                var after = await store.Read(adapter); Validate(after);
                if (after.Id != current.Id || after.Driver != current.Driver || after.Placement != target || after.Enabled != current.Enabled)
                    throw new IOException("RSS placement verification failed; original retained.");
                if (!apply) restore.ClearBackup(id);
                return TweakActionResult.Ok(apply ? "RSS processor limits applied and verified." : "Original RSS profile and processor limits restored.");
            }
            finally { gate.Release(); }
        }
    }
    // A failed driver call may have changed only some fields. Recover that mixed state while
    // refusing any value that is neither the saved original nor Elite's requested target.
    private static bool Compatible(RssPlacement value, RssPlacement original, RssPlacement target) =>
        (value.Profile == original.Profile || value.Profile == target.Profile) &&
        (value.BaseGroup == original.BaseGroup || value.BaseGroup == target.BaseGroup) &&
        (value.BaseNumber == original.BaseNumber || value.BaseNumber == target.BaseNumber) &&
        (value.MaxGroup == original.MaxGroup || value.MaxGroup == target.MaxGroup) &&
        (value.MaxNumber == original.MaxNumber || value.MaxNumber == target.MaxNumber) &&
        (value.MaxProcessors == original.MaxProcessors || value.MaxProcessors == target.MaxProcessors);
}
