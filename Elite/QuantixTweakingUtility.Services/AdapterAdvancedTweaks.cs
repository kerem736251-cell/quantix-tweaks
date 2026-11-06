using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using QuantixTweakingUtility.Models;

namespace QuantixTweakingUtility.Services;

public record AdapterProperty(string Key, string Value, string[] Allowed, string Minimum, string Maximum, string Step);
public record AdapterProperties(Guid Id, string Name, string Driver, AdapterProperty[] Properties);
public interface IAdapterPropertyStore
{
    Task<AdapterProperties> Read(bool fresh = false);
    Task Write(Guid id, IReadOnlyDictionary<string, string> values);
}

public sealed class WindowsAdapterPropertyStore : IAdapterPropertyStore
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private AdapterProperties? cached;
    private DateTimeOffset readAt;
    public async Task<AdapterProperties> Read(bool fresh = false)
    {
        await gate.WaitAsync();
        try
        {
            if (!fresh && cached != null && DateTimeOffset.UtcNow - readAt < TimeSpan.FromSeconds(5)) return cached;
            var inspection = await NetworkWorkflow.RunAsync("Inspect");
            if (!inspection.Success || inspection.Connection is not { Physical: true, Type: "Ethernet" } connection || !Guid.TryParse(connection.Id, out var id))
                throw new IOException("An active physical Ethernet adapter is required.");
            var script = WindowsAdapterRssStore.Find(id) + ";$driver=[string]$adapter.DriverVersion;if(!$driver){$driver=[string]$adapter.DriverVersionString};if(!$driver){throw 'Driver identity unavailable.'};$properties=@($adapter | Get-NetAdapterAdvancedProperty -AllProperties -ErrorAction Stop | Where-Object {$null -ne $_.RegistryValue -and @($_.RegistryValue).Count -eq 1} | ForEach-Object {[pscustomobject]@{Key=[string]$_.RegistryKeyword;Value=[string]$_.RegistryValue[0];Allowed=[string[]]@($_.ValidRegistryValues);Minimum=[string]$_.NumericParameterMinValue;Maximum=[string]$_.NumericParameterMaxValue;Step=[string]$_.NumericParameterStepValue}});[pscustomobject]@{Id=([guid]$adapter.InterfaceGuid).ToString('D');Name=[string]$adapter.Name;Driver=$driver;Properties=$properties}|ConvertTo-Json -Depth 5 -Compress";
            cached = JsonSerializer.Deserialize<AdapterProperties>(await WindowsAdapterRssStore.Run(script)) ?? throw new IOException("Driver properties were unavailable.");
            readAt = DateTimeOffset.UtcNow;
            return cached;
        }
        finally { gate.Release(); }
    }
    public async Task Write(Guid id, IReadOnlyDictionary<string, string> values)
    {
        if (values.Count == 0 || values.Any(item => !AdapterAdvancedTweaks.Keys.Contains(item.Key, StringComparer.Ordinal)
            || !int.TryParse(item.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var number) || number < 0))
            throw new IOException("Invalid driver property request.");
        var script = new StringBuilder(WindowsAdapterRssStore.Find(id));
        foreach (var item in values)
            script.Append(";$property=@($adapter|Get-NetAdapterAdvancedProperty -AllProperties -ErrorAction Stop|Where-Object {$_.RegistryKeyword -eq '")
                .Append(item.Key).Append("'});if($property.Count -ne 1){throw 'Driver property unavailable.'};$property|Set-NetAdapterAdvancedProperty -RegistryValue '")
                .Append(item.Value).Append("' -NoRestart -Confirm:$false -ErrorAction Stop");
        script.Append(";$adapter|Restart-NetAdapter -Confirm:$false -ErrorAction Stop");
        try { await WindowsAdapterRssStore.Run(script.ToString()); }
        finally { cached = null; }
    }
}

public static class AdapterAdvancedTweaks
{
    internal static readonly string[] Keys = { "*InterruptModeration", "*EEE", "*ReceiveBuffers", "*TransmitBuffers" };
    private sealed record Backup(Guid Id, string Driver, Dictionary<string, string> Original, Dictionary<string, string> Target);
    public static IEnumerable<TweakDefinition> Build(IRestoreService restore, IAdapterPropertyStore? store = null)
    {
        store ??= new WindowsAdapterPropertyStore();
        var gate = new SemaphoreSlim(1, 1);
        yield return Make("interrupt-moderation", "NIC Interrupt Moderation Off", "Disables the driver's standard interrupt moderation option. Can reduce interrupt batching at the cost of higher CPU/interrupt load. Compare latency under your actual workload.", new[] { Keys[0] }, false);
        yield return Make("eee", "Energy-Efficient Ethernet Off", "Disables the driver's standard Energy-Efficient Ethernet option. May help diagnose link wake-up delays, while increasing adapter power use. Other vendor-specific power-saving options are separate.", new[] { Keys[1] }, false);
        yield return Make("buffers", "NIC Buffer Boost", "Doubles receive and transmit buffer counts, capped and aligned to the values the driver advertises. More buffers use additional memory and can change latency. Both standard buffer settings must be exposed by the driver.", new[] { Keys[2], Keys[3] }, true);

        TweakDefinition Make(string suffix, string name, string detail, string[] keys, bool buffers)
        {
            string id = "network.adapter." + suffix;
            return new TweakDefinition
            {
                Id = id, Name = name, Description = detail + " Applies to the active Ethernet adapter. The adapter restarts and connections briefly drop. Originals are saved for this adapter and driver version.",
                Category = TweakCategory.Network, RiskLevel = TweakRiskLevel.High, RequiresAdministrator = true,
                AffectedSettings = keys.Select(key => "NetAdapter:active:" + key).ToArray(),
                CurrentStateReader = async () =>
                {
                    try
                    {
                        var current = await store.Read();
                        var original = Load();
                        var targets = original?.Target ?? Plan(current, keys, buffers);
                        Validate(current, original, keys);
                        return targets.All(item => Property(current, item.Key).Value == item.Value) ? TweakState.Applied : TweakState.NotApplied;
                    }
                    catch (IOException) { return TweakState.Unsupported; }
                    catch (JsonException) { return TweakState.Unknown; }
                },
                ApplyAction = () => Change(true), RestoreAction = () => Change(false)
            };

            Backup? Load()
            {
                var json = restore.GetBackup(id);
                return json == null ? null : JsonSerializer.Deserialize<Backup>(json) ?? throw new IOException("Invalid adapter backup.");
            }
            async Task<TweakActionResult> Change(bool apply)
            {
                await gate.WaitAsync();
                try
                {
                    var current = await store.Read(true);
                    var original = Load();
                    Validate(current, original, keys);
                    if (!apply && original == null) return TweakActionResult.Fail("No original driver values were saved by Elite.");
                    if (original == null)
                    {
                        var targets = Plan(current, keys, buffers);
                        original = new(current.Id, current.Driver, keys.ToDictionary(key => key, key => Property(current, key).Value), targets);
                        restore.SaveBackup(id, JsonSerializer.Serialize(original));
                    }
                    if (keys.Any(key => Property(current, key).Value != original.Original[key] && Property(current, key).Value != original.Target[key]))
                        throw new IOException("A driver property changed outside Elite. The original backup has been retained.");
                    var desired = apply ? original.Target : original.Original;
                    foreach (var item in desired) if (!Accepts(Property(current, item.Key), item.Value)) throw new IOException("The driver no longer accepts a saved value. The backup has been retained.");
                    if (desired.Any(item => Property(current, item.Key).Value != item.Value)) await store.Write(current.Id, desired);
                    AdapterProperties? verified = null;
                    for (int attempt = 0; attempt < 5; attempt++)
                    {
                        try
                        {
                            verified = await store.Read(true);
                            if (verified.Id == current.Id && verified.Driver == current.Driver && desired.All(item => Property(verified, item.Key).Value == item.Value)) break;
                        }
                        catch (IOException) { }
                        if (attempt < 4) await Task.Delay(500);
                    }
                    if (verified == null || verified.Id != current.Id || verified.Driver != current.Driver || desired.Any(item => Property(verified, item.Key).Value != item.Value))
                        throw new IOException("Driver readback did not match after restarting the adapter. The original backup has been retained.");
                    if (!apply) restore.ClearBackup(id);
                    return TweakActionResult.Ok(name + (apply ? " applied and verified." : " restored and verified."));
                }
                finally { gate.Release(); }
            }
        }
    }
    private static void Validate(AdapterProperties current, Backup? backup, string[] keys)
    {
        if (current.Id == Guid.Empty || string.IsNullOrWhiteSpace(current.Driver)) throw new IOException("Adapter identity unavailable.");
        foreach (string key in keys) _ = Property(current, key);
        if (backup == null) return;
        if (backup.Id != current.Id || backup.Driver != current.Driver) throw new IOException("The adapter or driver differs from the saved original. Backup retained.");
        if (backup.Original == null || backup.Target == null || backup.Original.Count != keys.Length || backup.Target.Count != keys.Length
            || keys.Any(key => !backup.Original.ContainsKey(key) || !backup.Target.ContainsKey(key))) throw new IOException("Invalid saved driver properties.");
    }
    private static AdapterProperty Property(AdapterProperties snapshot, string key) =>
        snapshot.Properties.SingleOrDefault(property => property.Key == key) ?? throw new IOException("This driver does not expose " + key + ".");
    private static Dictionary<string, string> Plan(AdapterProperties snapshot, string[] keys, bool buffers)
    {
        var plan = new Dictionary<string, string>();
        foreach (string key in keys)
        {
            var property = Property(snapshot, key);
            if (!Accepts(property, property.Value)) throw new IOException("The driver does not advertise the current value as restorable.");
            string target = "0";
            if (buffers)
            {
                if (!Number(property.Value, out int value) || value < 1) throw new IOException("Invalid current buffer count.");
                long doubled = (long)value * 2;
                var choices = (property.Allowed ?? Array.Empty<string>()).Select(text => Number(text, out int n) ? n : -1).Where(n => n >= value && n <= doubled).ToArray();
                if (choices.Length > 0) target = choices.Max().ToString(CultureInfo.InvariantCulture);
                else if (Number(property.Minimum, out int min) && Number(property.Maximum, out int max) && Number(property.Step, out int step) && step > 0 && max >= value && min <= value)
                {
                    long limit = Math.Min(doubled, max);
                    target = (min + (limit - min) / step * step).ToString(CultureInfo.InvariantCulture);
                }
                else throw new IOException("The driver does not advertise valid buffer limits.");
            }
            if (!Accepts(property, target)) throw new IOException("The driver does not support the requested value.");
            plan.Add(key, target);
        }
        return plan;
    }
    private static bool Number(string? value, out int number) => int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out number) && number >= 0;
    private static bool Accepts(AdapterProperty property, string value)
    {
        if (!Number(value, out int number)) return false;
        if (property.Allowed?.Contains(value, StringComparer.Ordinal) == true) return true;
        return Number(property.Minimum, out int min) && Number(property.Maximum, out int max) && Number(property.Step, out int step)
            && step > 0 && number >= min && number <= max && (number - min) % step == 0;
    }
}

