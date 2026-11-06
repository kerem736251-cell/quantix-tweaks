using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using QuantixTweakingUtility.Models;

namespace QuantixTweakingUtility.Services;

public interface IStartupPublisherReview
{
    Task<IReadOnlyList<StartupRunValue>> ThirdParty(IReadOnlyList<StartupRunValue> entries);
}

public sealed class WindowsStartupPublisherReview : IStartupPublisherReview
{
    // Commands are inspected as data, never launched. Ambiguous commands stay in Startup Manager.
    public static string? Executable(string command)
    {
        command = Environment.ExpandEnvironmentVariables(command).Trim();
        string path;
        if (command.StartsWith('"'))
        {
            int end = command.IndexOf('"', 1);
            if (end < 2 || (end + 1 < command.Length && !char.IsWhiteSpace(command[end + 1]))) return null;
            path = command.Substring(1, end - 1);
        }
        else path = command.Split(new[] { ' ', '\t', '\r', '\n' }, 2)[0];
        if (path.Length < 4 || !char.IsAsciiLetter(path[0]) || path[1] != ':' || path[2] != '\\'
            || !path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || path.Contains('\0')) return null;
        try
        {
            path = Path.GetFullPath(path);
            var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows).TrimEnd('\\') + "\\";
            if (path.StartsWith(windows, StringComparison.OrdinalIgnoreCase)) return null;
            if (new DriveInfo(Path.GetPathRoot(path)!).DriveType != DriveType.Fixed) return null;
            return path;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException) { return null; }
    }

    public async Task<IReadOnlyList<StartupRunValue>> ThirdParty(IReadOnlyList<StartupRunValue> entries)
    {
        var candidates = entries.Select(entry => (Entry: entry, Path: Executable(entry.Command)))
            .Where(item => item.Path != null && File.Exists(item.Path)).ToArray();
        if (candidates.Length == 0) return Array.Empty<StartupRunValue>();
        string data = Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(candidates.Select(item => item.Path).Distinct(StringComparer.OrdinalIgnoreCase))));
        string script = "Import-Module ($PSHOME+'\\Modules\\Microsoft.PowerShell.Security\\Microsoft.PowerShell.Security.psd1') -ErrorAction Stop;"
            + "$paths=[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('" + data + "'))|ConvertFrom-Json;"
            + "$result=@(foreach($path in $paths){$s=Get-AuthenticodeSignature -LiteralPath $path -ErrorAction Stop;"
            + "$subject=if($s.SignerCertificate){$s.SignerCertificate.Subject}else{''};"
            + "$microsoft=$subject -match '(?i)(^|,\\s*)O=\"?Microsoft Corporation\"?(,|$)';"
            + "[pscustomobject]@{Path=[string]$path;Eligible=[bool](!$microsoft -and ($s.Status -eq 'Valid' -or $s.Status -eq 'NotSigned'))}});"
            + "ConvertTo-Json -InputObject $result -Compress";
        var reviewed = JsonSerializer.Deserialize<PublisherResult[]>(await WindowsAdapterRssStore.Run(script))
            ?? throw new IOException("Startup publisher review returned no result.");
        if (reviewed.Length != candidates.Select(item => item.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count())
            throw new IOException("Startup publisher review was incomplete. No startup entries removed.");
        var eligible = reviewed.Where(item => item.Eligible).Select(item => item.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return candidates.Where(item => eligible.Contains(item.Path!)).Select(item => item.Entry).ToArray();
    }
    private sealed record PublisherResult(string Path, bool Eligible);
}

public static class StartupFirewallTweak
{
    public static TweakDefinition Build(IStartupRunQuarantine? store = null, IStartupPublisherReview? review = null)
    {
        store ??= new StartupRunQuarantine(); review ??= new WindowsStartupPublisherReview();
        var gate = new SemaphoreSlim(1, 1);
        return new TweakDefinition
        {
            Id = "processes.startup-quarantine", Name = "Startup Firewall",
            Description = "Quarantines third-party Run entries for this user and the machine, preventing their next sign-in launch. Includes unsigned local apps; preserves Microsoft publishers and Windows executables. Ambiguous commands, missing files, scripts and network paths stay unchanged. Does not close running apps or block new entries. Restore recovers each exact saved command and type. Startup apps such as peripheral tools or security utilities may stop launching; review Startup Manager first.",
            Category = TweakCategory.Windows, RiskLevel = TweakRiskLevel.High, RequiresAdministrator = true,
            AffectedSettings = new[] { "HKCU/HKLM:Software\\Microsoft\\Windows\\CurrentVersion\\Run:third-party entries" },
            HasSavedOriginal = () => store.Originals().Count > 0,
            CurrentStateReader = async () =>
            {
                var originals = store.Originals();
                var remaining = await review.ThirdParty(store.Entries());
                return remaining.Count > 0 ? TweakState.NotApplied : originals.Count > 0 ? TweakState.Applied : TweakState.Unsupported;
            },
            ApplyAction = () => Change(true), RestoreAction = () => Change(false)
        };
        async Task<TweakActionResult> Change(bool apply)
        {
            await gate.WaitAsync();
            try
            {
                var entries = apply ? await review.ThirdParty(store.Entries()) : store.Originals();
                if (entries.Count == 0) return TweakActionResult.Fail(apply ? "No eligible third-party Run entries were found." : "No startup originals were saved by Elite.");
                int done = 0; var errors = new List<string>();
                foreach (var entry in entries)
                {
                    try { if (apply) store.Quarantine(entry); else store.Restore(entry); done++; }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
                    { errors.Add(entry.Name + ": " + ex.Message); }
                }
                string result = done + " startup " + (done == 1 ? "entry " : "entries ") + (apply ? "quarantined." : "restored.");
                return errors.Count == 0 ? TweakActionResult.Ok(result) : TweakActionResult.Fail(result + " Remaining originals retained. " + string.Join(" ", errors));
            }
            finally { gate.Release(); }
        }
    }
}
