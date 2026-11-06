using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using QuantixTweakingUtility.Models;

namespace QuantixTweakingUtility.Services;

public enum MemoryAgentSetting { MemoryCompression, PageCombining }
public interface IMemoryAgentStore
{
    Task<bool> Read(MemoryAgentSetting setting);
    Task Write(MemoryAgentSetting setting, bool enabled);
}
public sealed class WindowsMemoryAgentStore : IMemoryAgentStore
{
    private static string Parameter(MemoryAgentSetting setting) => setting switch
    {
        MemoryAgentSetting.MemoryCompression => "MemoryCompression",
        MemoryAgentSetting.PageCombining => "PageCombining",
        _ => throw new IOException("Unknown memory-management setting.")
    };
    public async Task<bool> Read(MemoryAgentSetting setting)
    {
        string parameter = Parameter(setting);
        RequireAdministrator();
        // Do not collapse access denied, absent properties or malformed output into False.
        var json = await WindowsAdapterRssStore.Run("$value=(Get-MMAgent -ErrorAction Stop)." + parameter +
            ";if($value -isnot [bool]){throw 'Windows did not return a Boolean memory-management state.'};$value|ConvertTo-Json -Compress");
        return Parse(json);
    }
    public static bool Parse(string json)
    {
        try
        {
            using var value = JsonDocument.Parse(json);
            return value.RootElement.ValueKind switch
            {
                JsonValueKind.True => true, JsonValueKind.False => false,
                _ => throw new IOException("Windows returned an invalid memory-management state.")
            };
        }
        catch (JsonException ex) { throw new IOException("Windows returned unreadable memory-management data.", ex); }
    }
    public async Task Write(MemoryAgentSetting setting, bool enabled)
    {
        string parameter = Parameter(setting);
        RequireAdministrator();
        await WindowsAdapterRssStore.Run((enabled ? "Enable" : "Disable") + "-MMAgent -" + parameter + " -ErrorAction Stop");
    }
    private static void RequireAdministrator()
    {
        if (!AdminHelper.IsRunningAsAdministrator()) throw new UnauthorizedAccessException("Launch Elite as administrator to read or change Windows memory-management settings.");
    }
}

public static class MemoryAgentTweaks
{
    public static TweakDefinition Build(MemoryAgentSetting setting, IRestoreService restore, IMemoryAgentStore? store = null)
    {
        if (!Enum.IsDefined(setting)) throw new ArgumentOutOfRangeException(nameof(setting));
        store ??= new WindowsMemoryAgentStore();
        bool compression = setting == MemoryAgentSetting.MemoryCompression;
        string id = compression ? "memory.compression.disable" : "memory.page-combining.disable";
        var gate = new SemaphoreSlim(1, 1);
        return new TweakDefinition
        {
            Id = id, Name = compression ? "Disable Memory Compression" : "Disable Memory Page Combining",
            Description = compression
                ? "Disables Windows in-RAM page compression through MMAgent and verifies the resulting state. This can increase RAM use, paging and stutter under memory pressure; performance gains are not guaranteed. Saves the original enabled state for restore."
                : "Disables periodic merging of identical memory pages through MMAgent and verifies the resulting state. This may reduce background scanning but increase RAM use; performance gains are not guaranteed. Saves the original enabled state for restore.",
            Category = TweakCategory.Memory, RiskLevel = TweakRiskLevel.Medium, RequiresAdministrator = true,
            AffectedSettings = new[] { compression ? "mmagent:memory-compression" : "mmagent:page-combining" },
            HasSavedOriginal = () => restore.GetBackup(id) != null,
            CurrentStateReader = async () => await store.Read(setting) ? TweakState.NotApplied : TweakState.Applied,
            ApplyAction = () => Change(true), RestoreAction = () => Change(false)
        };
        async Task<TweakActionResult> Change(bool apply)
        {
            await gate.WaitAsync();
            try
            {
                string? raw = restore.GetBackup(id);
                if (raw != null && !bool.TryParse(raw, out _)) throw new IOException("Saved memory-management state is invalid; retained for review.");
                if (!apply && raw == null) return TweakActionResult.Fail("No original memory-management state was saved.");
                bool current = await store.Read(setting);
                if (raw == null)
                {
                    raw = current.ToString(); restore.SaveBackup(id, raw);
                    if (restore.GetBackup(id) != raw) throw new IOException("Cannot verify saved original; no memory-management change requested.");
                }
                bool target = !apply && bool.Parse(raw);
                if (current != target) await store.Write(setting, target);
                if (await store.Read(setting) != target) throw new IOException("Memory-management readback failed; original retained.");
                if (!apply) restore.ClearBackup(id);
                return TweakActionResult.Ok("Memory-management state " + (apply ? "disabled" : "restored") + " and verified.");
            }
            finally { gate.Release(); }
        }
    }
}
