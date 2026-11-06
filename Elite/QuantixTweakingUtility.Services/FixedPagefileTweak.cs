using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using QuantixTweakingUtility.Models;

namespace QuantixTweakingUtility.Services;

public record ActivePagefile(string Path, int Megabytes);
public record PagefileConfiguration(bool Automatic, string[] Files);
public record PagefileState(PagefileConfiguration Configuration, ActivePagefile[] Active);
public record PagefileOriginal(PagefileConfiguration Original, PagefileConfiguration Target);
public interface IPagefileStore
{
    Task<PagefileState> Read();
    Task Write(PagefileConfiguration expected, PagefileConfiguration target);
}
public sealed class WindowsPagefileStore : IPagefileStore
{
    private const string Inspect = "$system=@(Get-CimInstance Win32_ComputerSystem -ErrorAction Stop);if($system.Count -ne 1 -or $null -eq $system[0].AutomaticManagedPagefile){throw 'Pagefile management state unavailable.'};$system=$system[0];" +
        @"$key=[Microsoft.Win32.Registry]::LocalMachine.OpenSubKey('SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management');try{if(!$key -or $key.GetValueKind('PagingFiles') -ne [Microsoft.Win32.RegistryValueKind]::MultiString){throw 'Unsupported PagingFiles registry type.'};$files=[string[]]$key.GetValue('PagingFiles')}finally{if($key){$key.Dispose()}};";
    public async Task<PagefileState> Read()
    {
        var json = await WindowsAdapterRssStore.Run(Inspect +
            "[pscustomobject]@{Configuration=[pscustomobject]@{Automatic=[bool]$system.AutomaticManagedPagefile;Files=@($files)};Active=@(Get-CimInstance Win32_PageFileUsage -ErrorAction Stop | ForEach-Object {[pscustomobject]@{Path=[string]$_.Name;Megabytes=[int]$_.AllocatedBaseSize}})}|ConvertTo-Json -Depth 5 -Compress");
        return JsonSerializer.Deserialize<PagefileState>(json) ?? throw new IOException("Pagefile state unavailable.");
    }
    public async Task Write(PagefileConfiguration expected, PagefileConfiguration target)
    {
        FixedPagefileTweak.Validate(expected); FixedPagefileTweak.Validate(target);
        string Data(PagefileConfiguration value) => Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value)));
        // Configuration is encoded data, never interpolated as executable paths/commands.
        string script = "$expected=([Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('" + Data(expected) + "'))|ConvertFrom-Json);$target=([Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('" + Data(target) + "'))|ConvertFrom-Json);" + Inspect +
            "if([bool]$system.AutomaticManagedPagefile -ne [bool]$expected.Automatic -or $files.Count -ne @($expected.Files).Count){throw 'Pagefile configuration changed before write.'};for($i=0;$i -lt $files.Count;$i++){if($files[$i] -cne $expected.Files[$i]){throw 'Pagefile entries changed before write.'}};" +
            "if([bool]$system.AutomaticManagedPagefile -ne [bool]$target.Automatic){$system|Set-CimInstance -Property @{AutomaticManagedPagefile=[bool]$target.Automatic} -ErrorAction Stop|Out-Null};" +
            @"$key=[Microsoft.Win32.Registry]::LocalMachine.OpenSubKey('SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management',$true);try{if(!$key){throw 'Memory-management registry key unavailable.'};$key.SetValue('PagingFiles',[string[]]@($target.Files),[Microsoft.Win32.RegistryValueKind]::MultiString);$key.Flush()}finally{if($key){$key.Dispose()}};";
        await WindowsAdapterRssStore.Run(script);
    }
}

public static class FixedPagefileTweak
{
    internal static void Validate(PagefileConfiguration value)
    {
        if (value == null || value.Files == null || value.Files.Length > 32 || value.Files.Any(file => file == null || file.Length > 1024 || file.Contains('\0') || file.Contains('\r') || file.Contains('\n')))
            throw new IOException("Invalid pagefile configuration; no settings guessed.");
    }
    private static bool Same(PagefileConfiguration left, PagefileConfiguration right) => left.Automatic == right.Automatic && left.Files.SequenceEqual(right.Files, StringComparer.Ordinal);
    private static PagefileConfiguration Target(PagefileState state)
    {
        Validate(state.Configuration);
        if (state.Active == null || state.Active.Length is 0 or > 16 || state.Active.Any(file => file == null || !Regex.IsMatch(file.Path, @"^[A-Za-z]:\\pagefile\.sys$", RegexOptions.IgnoreCase) || file.Megabytes is < 16 or > 4194304) || state.Active.Select(file => file.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() != state.Active.Length)
            throw new IOException("Active pagefile sizes and supported local paths are required; no arbitrary size was selected.");
        return new(false, state.Active.OrderBy(file => file.Path, StringComparer.OrdinalIgnoreCase).Select(file => $"{file.Path} {file.Megabytes} {file.Megabytes}").ToArray());
    }
    public static TweakDefinition Build(IRestoreService restore, IPagefileStore? store = null)
    {
        store ??= new WindowsPagefileStore(); const string id = "storage.pagefile.fixed"; var gate = new SemaphoreSlim(1, 1);
        return new TweakDefinition
        {
            Id = id, Name = "Fixed Pagefile", Category = TweakCategory.Storage,
            RiskLevel = TweakRiskLevel.High, RequiresAdministrator = true, RequiresRestart = true,
            Description = "Disables automatic pagefile sizing and fixes each active pagefile's initial and maximum size to its current allocation in MB. Requires supported local pagefile paths. A fixed limit may cause out-of-memory failures or prevent complete crash dumps; no FPS gain is guaranteed. Saves automatic-management mode and the exact configured entries for restore. Restart after Apply or Restore; active allocation is not changed immediately.",
            AffectedSettings = new[] { "MemoryManagement:PagingFiles", "ComputerSystem:AutomaticManagedPagefile" },
            HasSavedOriginal = () => restore.GetBackup(id) != null,
            CurrentStateReader = async () =>
            {
                var state = await store.Read(); var target = Target(state);
                return Same(state.Configuration, target) ? TweakState.Applied : TweakState.NotApplied;
            },
            ApplyAction = () => Change(true), RestoreAction = () => Change(false)
        };
        async Task<TweakActionResult> Change(bool apply)
        {
            await gate.WaitAsync();
            try
            {
                string? raw = restore.GetBackup(id); PagefileOriginal? saved;
                try { saved = raw == null ? null : JsonSerializer.Deserialize<PagefileOriginal>(raw); }
                catch (JsonException ex) { throw new IOException("Pagefile originals are invalid; retained for review.", ex); }
                if (raw != null && saved == null) throw new IOException("Invalid pagefile backup.");
                if (saved != null) { Validate(saved.Original); Validate(saved.Target); if (saved.Target.Automatic || saved.Target.Files.Length == 0) throw new IOException("Invalid saved fixed pagefile target."); }
                if (!apply && saved == null) return TweakActionResult.Fail("No original pagefile configuration was saved.");
                var current = await store.Read(); Validate(current.Configuration);
                if (saved == null)
                {
                    saved = new(current.Configuration, Target(current)); raw = JsonSerializer.Serialize(saved); restore.SaveBackup(id, raw);
                    if (restore.GetBackup(id) != raw) throw new IOException("Cannot verify pagefile originals; no changes requested.");
                }
                // A failed native call can have changed management mode before writing the configured entries.
                if (!current.Configuration.Files.SequenceEqual(saved.Original.Files) && !current.Configuration.Files.SequenceEqual(saved.Target.Files))
                    throw new IOException("Pagefile entries changed outside the recorded operation; originals retained for review.");
                var target = apply ? saved.Target : saved.Original;
                if (!Same(current.Configuration, target)) await store.Write(current.Configuration, target);
                var after = await store.Read(); Validate(after.Configuration);
                if (!Same(after.Configuration, target)) throw new IOException("Pagefile configuration readback failed; originals retained.");
                if (!apply) restore.ClearBackup(id);
                return TweakActionResult.Ok("Pagefile configuration " + (apply ? "fixed" : "restored") + " and read back. Restart Windows for the allocation change.");
            }
            finally { gate.Release(); }
        }
    }
}
