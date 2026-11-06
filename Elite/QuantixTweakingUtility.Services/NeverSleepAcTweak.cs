using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using QuantixTweakingUtility.Models;

namespace QuantixTweakingUtility.Services;

public static class NeverSleepAcTweak
{
    public record Policy(Guid Group, Guid Setting);
    public static IReadOnlyList<Policy> Policies { get; } = Array.AsReadOnly(new[]
    {
        new Policy(new("7516b95f-f776-4464-8c53-06167f40cc99"), new("3c0bc021-c8a8-4e07-a973-6b14cbcb2b7e")),
        new Policy(new("238c9fa8-0aad-41ed-83f4-97be242c8f20"), new("29f6c1db-86da-48c5-9fdb-f2b67b1f44da")),
        new Policy(new("238c9fa8-0aad-41ed-83f4-97be242c8f20"), new("bd3b718a-0680-4d9d-8ab2-e1d2b4ac806d"))
    });
    private const string Id = "power.policy.never-sleep-ac";
    private sealed record Original(Guid Scheme, Dictionary<Guid,uint> Values);
    public static TweakDefinition Build(IRestoreService restore, IPowerPolicyStore? store = null)
    {
        store ??= new WindowsPowerPolicyStore(); var gate = new object();
        return new TweakDefinition
        {
            Id = Id, Name = "Never Sleep on AC", Category = TweakCategory.Cpu,
            Description = "Sets the current plan's plugged-in display-off and idle-sleep timeouts to Never and disables wake timers. Scheduled maintenance cannot wake the PC through those timers while plugged in. Battery values remain unchanged. Each original value is saved for the same power plan; explicit sleep/shutdown commands and organization policies still apply.",
            RiskLevel = TweakRiskLevel.Medium, RequiresAdministrator = true,
            AffectedSettings = Policies.Select(item => $"powercfg:AC:{item.Group}:{item.Setting}").ToArray(),
            CurrentStateReader = () =>
            {
                try { var scheme = store.ActiveScheme(); return Task.FromResult(Policies.All(item => store.Read(scheme,item.Group,item.Setting) == 0) ? TweakState.Applied : TweakState.NotApplied); }
                catch (Win32Exception) { return Task.FromResult(TweakState.Unsupported); }
            },
            ApplyAction = () => Change(true), RestoreAction = () => Change(false)
        };
        Task<TweakActionResult> Change(bool apply)
        {
            lock(gate)
            {
                var scheme = store.ActiveScheme();
                if(scheme == Guid.Empty) throw new IOException("No valid active power plan is available.");
                var current = Policies.ToDictionary(item => item.Setting, item => store.Read(scheme,item.Group,item.Setting));
                if(current[Policies[2].Setting] > 2) throw new IOException("Wake timers have an unsupported value. No settings changed.");
                string? saved = restore.GetBackup(Id); Original? original;
                try { original = saved == null ? null : JsonSerializer.Deserialize<Original>(saved); }
                catch(JsonException ex) { throw new IOException("The AC timeout backup is unreadable. No settings changed.",ex); }
                if(saved != null && (original == null || original.Scheme == Guid.Empty || original.Values == null || original.Values.Count != Policies.Count
                    || Policies.Any(item => !original.Values.ContainsKey(item.Setting)) || original.Values[Policies[2].Setting] > 2))
                    throw new IOException("The AC timeout backup is invalid. No settings changed.");
                if(original != null && original.Scheme != scheme) throw new IOException("Activate the original power plan before restoring or reapplying AC timeouts. Backup retained.");
                if(!apply && original == null) return Task.FromResult(TweakActionResult.Fail("No original AC timeout values were saved by Elite."));
                if(apply && original == null)
                {
                    original = new(scheme,current); restore.SaveBackup(Id,JsonSerializer.Serialize(original));
                    var verified = JsonSerializer.Deserialize<Original>(restore.GetBackup(Id) ?? "null");
                    if(verified == null || verified.Scheme != scheme || verified.Values == null || verified.Values.Count != current.Count
                        || current.Any(item => !verified.Values.TryGetValue(item.Key,out var value) || value != item.Value))
                        throw new IOException("AC timeout backup verification failed. No settings changed.");
                }
                int done = 0; var errors = new List<string>();
                foreach(var item in Policies)
                {
                    try
                    {
                        if(store.ActiveScheme() != scheme) throw new IOException("The active power plan changed. Backup retained.");
                        uint now = store.Read(scheme,item.Group,item.Setting);
                        if(now != 0 && now != original!.Values[item.Setting]) throw new IOException("An AC power value changed outside Elite. Backup retained.");
                        uint target = apply ? 0 : original!.Values[item.Setting];
                        if(now != target) store.Write(scheme,item.Group,item.Setting,target);
                        if(store.Read(scheme,item.Group,item.Setting) != target) throw new IOException("AC timeout readback failed. Backup retained.");
                        done++;
                    }
                    catch(Exception ex) when(ex is IOException or Win32Exception) { errors.Add(ex.Message); }
                }
                store.Refresh(scheme);
                if(!apply && errors.Count == 0) restore.ClearBackup(Id);
                string message = done + " AC power values " + (apply ? "set to zero and verified." : "restored and verified.");
                return Task.FromResult(errors.Count == 0 ? TweakActionResult.Ok(message) : TweakActionResult.Fail(message + " " + string.Join(" ",errors.Distinct())));
            }
        }
    }
}
