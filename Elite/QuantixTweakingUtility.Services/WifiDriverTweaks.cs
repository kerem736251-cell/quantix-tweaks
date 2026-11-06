using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using QuantixTweakingUtility.Models;

namespace QuantixTweakingUtility.Services;

public record WifiProperty(string Key, string Value, string[] Values, string[] Labels);
public record WifiSnapshot(Guid Id, string Name, string Driver, WifiProperty[] Properties);
public interface IWifiDriverStore
{
    Task<WifiSnapshot> Read(Guid? id = null);
    Task Write(Guid id, string key, string value);
}
public sealed class WindowsWifiDriverStore : IWifiDriverStore
{
    private static string Find(Guid? id) => id.HasValue ? WindowsAdapterRssStore.Find(id.Value) +
        ";if([int]$adapter.NdisPhysicalMedium -notin @(1,9)){throw 'Original adapter is not Wi-Fi.'}" :
        "$adapter=@(Get-NetAdapter -Physical -ErrorAction Stop|Where-Object {[int]$_.NdisPhysicalMedium -in @(1,9)});if($adapter.Count -ne 1){throw 'Exactly one physical Wi-Fi adapter is required.'};$adapter=$adapter[0]";
    public async Task<WifiSnapshot> Read(Guid? id = null)
    {
        string script = Find(id) + ";$properties=@($adapter|Get-NetAdapterAdvancedProperty -AllProperties -ErrorAction Stop|Where-Object {$null -ne $_.RegistryValue -and @($_.RegistryValue).Count -eq 1}|ForEach-Object {[pscustomobject]@{Key=[string]$_.RegistryKeyword;Value=[string]$_.RegistryValue[0];Values=[string[]]@($_.ValidRegistryValues);Labels=[string[]]@($_.ValidDisplayValues)}});[pscustomobject]@{Id=([guid]$adapter.InterfaceGuid).ToString('D');Name=[string]$adapter.Name;Driver=[string]$adapter.DriverVersion;Properties=$properties}|ConvertTo-Json -Depth 5 -Compress";
        return JsonSerializer.Deserialize<WifiSnapshot>(await WindowsAdapterRssStore.Run(script)) ?? throw new IOException("Wi-Fi driver properties unavailable.");
    }
    public async Task Write(Guid id, string key, string value)
    {
        if (!WifiDriverTweaks.AllowedKeys.Contains(key, StringComparer.Ordinal) || !uint.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out _))
            throw new IOException("Invalid Wi-Fi property request.");
        await WindowsAdapterRssStore.Run(Find(id) + ";$property=@($adapter|Get-NetAdapterAdvancedProperty -AllProperties -ErrorAction Stop|Where-Object {$_.RegistryKeyword -ceq '" + key +
            "'});if($property.Count -ne 1 -or '" + value + "' -notin @($property[0].ValidRegistryValues)){throw 'Wi-Fi driver no longer accepts the requested value.'};$property[0]|Set-NetAdapterAdvancedProperty -RegistryValue '" + value + "' -Confirm:$false -ErrorAction Stop");
    }
}

public static class WifiDriverTweaks
{
    internal static readonly string[] AllowedKeys = { "uAPSDSupport", "UAPSDSupport", "MIMOPowerSaveMode", "RoamingPreferredBandType", "PreferredBand", "RoamAggressiveness", "RoamIndicateTh", "IbssTxPower", "TxPowerLevel" };
    private sealed record Spec(string Suffix, string Name, string[] Keys, string Target, string Detail);
    private static readonly Spec[] Specs = {
        new("uapsd.disable", "Disable U-APSD Power Save", new[]{"uAPSDSupport","UAPSDSupport"}, "disabled", "Disables U-APSD power saving when exposed by the Wi-Fi driver. May help diagnose access-point compatibility issues while increasing power use."),
        new("mimo-power-save.disable", "Disable MIMO Power Save", new[]{"MIMOPowerSaveMode"}, "nosmps", "Selects No SMPS so all Wi-Fi receive antennas stay available. Uses more power; throughput improvement depends on the access point."),
        new("prefer-5ghz", "Prefer 5GHz Band", new[]{"RoamingPreferredBandType","PreferredBand"}, "prefer5ghzband", "Prefers 5GHz where the driver and access point support it. This is a preference, not a 5GHz-only lock; range can be shorter than 2.4GHz."),
        new("roaming.maximum", "Max Roaming Aggressiveness", new[]{"RoamAggressiveness","RoamIndicateTh"}, "highest", "Selects the driver's highest roaming aggressiveness. Scans for other access points sooner; may increase scanning and interruptions on a single-router network."),
        new("transmit.maximum", "Max Transmit Power", new[]{"IbssTxPower","TxPowerLevel"}, "highest", "Selects the highest Wi-Fi transmit-power level advertised by the driver, within its regulatory limits. Can increase interference and power use; does not change router transmit power.")
    };
    private sealed record Original(Guid Id, string Driver, string Key, string Value, string Target);
    public static IEnumerable<TweakDefinition> Build(IRestoreService restore, IWifiDriverStore? store = null)
    {
        store ??= new WindowsWifiDriverStore();
        var gate = new SemaphoreSlim(1,1);
        foreach(var spec in Specs) yield return Make(spec);
        TweakDefinition Make(Spec spec)
        {
            string id="network.wifi."+spec.Suffix;
            return new TweakDefinition {
                Id=id, Name=spec.Name, Category=TweakCategory.Network, RiskLevel=TweakRiskLevel.Medium, RequiresAdministrator=true,
                Description=spec.Detail+" Wi-Fi only; Ethernet is untouched. Requires exactly one physical Wi-Fi adapter for initial selection. Driver option labels must be recognized; unsupported or ambiguous options remain unavailable. The adapter may restart and briefly disconnect. Elite saves the original value and driver identity for restore.",
                AffectedSettings=spec.Keys.Select(key=>"NetAdapter:WiFi:"+key).ToArray(), HasSavedOriginal=()=>restore.GetBackup(id)!=null,
                CurrentStateReader=async()=>{
                    try{var snapshot=await store.Read();var (property,target)=Plan(snapshot,spec);return property.Value==target?TweakState.Applied:TweakState.NotApplied;}
                    catch(IOException){return TweakState.Unsupported;}
                }, ApplyAction=()=>Change(true),RestoreAction=()=>Change(false)
            };
            async Task<TweakActionResult> Change(bool apply)
            {
                await gate.WaitAsync();
                try{
                    string? raw=restore.GetBackup(id);Original? original;
                    try{original=raw==null?null:JsonSerializer.Deserialize<Original>(raw);}
                    catch(JsonException ex){throw new IOException("Unreadable Wi-Fi original; backup retained.",ex);}
                    if(raw!=null&&(original==null||original.Id==Guid.Empty||string.IsNullOrWhiteSpace(original.Driver)||!spec.Keys.Contains(original.Key,StringComparer.Ordinal)||!Numeric(original.Value)||!Numeric(original.Target)))
                        throw new IOException("Invalid saved Wi-Fi original; no settings changed.");
                    if(!apply&&original==null)return TweakActionResult.Fail("No original Wi-Fi driver value was saved by Elite.");
                    var snapshot=await store.Read(apply?null:original!.Id);
                    var (property,target)=Plan(snapshot,spec);
                    if(original!=null&&(snapshot.Id!=original.Id||snapshot.Driver!=original.Driver||property.Key!=original.Key||target!=original.Target))
                        throw new IOException("The Wi-Fi adapter, driver or option mapping changed. Original backup retained.");
                    if(original==null){
                        original=new(snapshot.Id,snapshot.Driver,property.Key,property.Value,target);raw=JsonSerializer.Serialize(original);
                        restore.SaveBackup(id,raw);if(restore.GetBackup(id)!=raw)throw new IOException("Wi-Fi backup verification failed; no settings changed.");
                    }
                    if(property.Value!=original.Value&&property.Value!=original.Target)throw new IOException("The Wi-Fi option changed outside Elite. Original backup retained.");
                    string desired=apply?original.Target:original.Value;
                    if(!property.Values.Contains(desired,StringComparer.Ordinal))throw new IOException("The driver no longer accepts the saved value. Original backup retained.");
                    if(property.Value!=desired)await store.Write(snapshot.Id,property.Key,desired);
                    bool verified=false;
                    for(int attempt=0;attempt<5;attempt++){
                        try{var after=await store.Read(snapshot.Id);var (afterProperty,afterTarget)=Plan(after,spec);verified=after.Id==snapshot.Id&&after.Driver==snapshot.Driver&&afterProperty.Key==property.Key&&afterProperty.Value==desired&&afterTarget==target;if(verified)break;}catch(IOException){}
                        if(attempt<4)await Task.Delay(500);
                    }
                    if(!verified)throw new IOException("Wi-Fi readback did not complete after the possible adapter restart. Original backup retained.");
                    if(!apply)restore.ClearBackup(id);
                    return TweakActionResult.Ok(spec.Name+(apply?" applied":" restored")+" and verified on "+snapshot.Name+".");
                }finally{gate.Release();}
            }
        }
    }
    private static (WifiProperty Property,string Target) Plan(WifiSnapshot snapshot, Spec spec)
    {
        if(snapshot.Id==Guid.Empty||string.IsNullOrWhiteSpace(snapshot.Driver)||snapshot.Properties==null)throw new IOException("Wi-Fi adapter identity unavailable.");
        var matches=snapshot.Properties.Where(p=>spec.Keys.Contains(p.Key,StringComparer.Ordinal)).ToArray();
        if(matches.Length!=1)throw new IOException("The driver does not expose an unambiguous Wi-Fi option.");
        var property=matches[0];
        if(property.Values==null||property.Labels==null||property.Values.Length==0||property.Values.Length!=property.Labels.Length||property.Values.Distinct(StringComparer.Ordinal).Count()!=property.Values.Length||property.Values.Any(v=>!Numeric(v))||!property.Values.Contains(property.Value,StringComparer.Ordinal))
            throw new IOException("The Wi-Fi driver does not advertise a restorable option mapping.");
        var targets=property.Labels.Select((label,index)=>(Label:Normalize(label),Index:index)).Where(p=>p.Label==spec.Target).ToArray();
        if(targets.Length!=1)throw new IOException("The Wi-Fi driver's option labels are not recognized or are ambiguous.");
        return (property,property.Values[targets[0].Index]);
    }
    private static bool Numeric(string? value)=>uint.TryParse(value,NumberStyles.None,CultureInfo.InvariantCulture,out _);
    private static string Normalize(string? label)=>Regex.Replace(Regex.Replace(label??"",@"^\s*\d+[.\-:]\s*",""),@"[\s\-]","").ToLowerInvariant();
}
