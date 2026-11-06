using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using QuantixTweakingUtility.Models;

namespace QuantixTweakingUtility.Services;

public record FirewallTask(string Path, bool Enabled, string Fingerprint, bool Microsoft);
public interface IFirewallTaskStore
{
    Task<IReadOnlyList<FirewallTask>> All();
    Task<FirewallTask> Read(string path);
    Task SetEnabled(string path, string fingerprint, bool enabled);
}

public sealed class WindowsFirewallTaskStore : IFirewallTaskStore
{
    public Task<IReadOnlyList<FirewallTask>> All() => Task.Run<IReadOnlyList<FirewallTask>>(() =>
    {
        dynamic service = Connect();
        try
        {
            var result = new List<FirewallTask>();
            dynamic root = service.GetFolder("\\");
            try { Walk(root, result); } finally { Release(root); }
            return result;
        }
        finally { Release(service); }
    });
    public Task<FirewallTask> Read(string path) => Task.Run<FirewallTask>(() => WithTask<FirewallTask>(path, task => Snapshot(task)));
    public Task SetEnabled(string path, string fingerprint, bool enabled) => Task.Run(() => WithTask(path, task =>
    {
        var before = Snapshot(task);
        if (before.Microsoft || before.Fingerprint != fingerprint) throw new IOException("The scheduled task identity changed. Original backup retained.");
        task.Enabled = enabled;
        var after = Snapshot(task);
        if (after.Enabled != enabled || after.Fingerprint != fingerprint) throw new IOException("Scheduled task readback did not match. Original backup retained.");
        return true;
    }));

    internal static Task SetTelemetryEnabled(string path,string fingerprint,bool enabled) => Task.Run(()=>
    {
        if(!WindowsTelemetryTasksTweak.Paths.Contains(path,StringComparer.OrdinalIgnoreCase))throw new IOException("This task is outside the explicit telemetry allowlist.");
        WithTask(path,task=>{
            var before=Snapshot(task);
            if(before.Fingerprint!=fingerprint)throw new IOException("The scheduled task definition changed. Original retained.");
            task.Enabled=enabled;
            var after=Snapshot(task);
            if(after.Fingerprint!=fingerprint||after.Enabled!=enabled)throw new IOException("Telemetry task readback failed. Original retained.");
            return true;
        });
    });

    private static T WithTask<T>(string path, Func<dynamic, T> action)
    {
        if (!path.StartsWith('\\') || path.EndsWith('\\')) throw new IOException("Invalid scheduled task path.");
        dynamic service = Connect();
        try
        {
            int split = path.LastIndexOf('\\');
            dynamic folder = service.GetFolder(split == 0 ? "\\" : path[..split]);
            try
            {
                dynamic task = folder.GetTask(path[(split + 1)..]);
                try { return action(task); } finally { Release(task); }
            }
            finally { Release(folder); }
        }
        finally { Release(service); }
    }
    private static object Connect()
    {
        dynamic service = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service") ?? throw new IOException("Windows Task Scheduler is unavailable."))!;
        try { service.Connect(); return service; }
        catch { Release(service); throw; }
    }
    private static void Walk(dynamic folder, List<FirewallTask> result)
    {
        dynamic tasks = folder.GetTasks(1); // TASK_ENUM_HIDDEN
        try
        {
            for (int i = 1; i <= tasks.Count; i++)
            {
                dynamic task = tasks[i];
                try { result.Add(Snapshot(task)); } finally { Release(task); }
            }
        }
        finally { Release(tasks); }
        dynamic children = folder.GetFolders(0);
        try
        {
            for (int i = 1; i <= children.Count; i++)
            {
                dynamic child = children[i];
                try
                {
                    // No traversal or mutation of the Microsoft task tree.
                    if (!IsMicrosoftPath((string)child.Path)) Walk(child, result);
                }
                finally { Release(child); }
            }
        }
        finally { Release(children); }
    }
    private static FirewallTask Snapshot(dynamic task) => Inspect((string)task.Path, (bool)task.Enabled, (string)task.Xml);
    public static FirewallTask Inspect(string path, bool enabled, string xml)
    {
        using var reader = XmlReader.Create(new StringReader(xml), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, IgnoreWhitespace = true });
        var document = XDocument.Load(reader);
        var root = document.Root ?? throw new IOException("Scheduled task definition is empty.");
        var ns = root.Name.Namespace;
        string author = (string?)root.Element(ns + "RegistrationInfo")?.Element(ns + "Author") ?? "";
        bool microsoft = IsMicrosoftPath(path) || author.Contains("Microsoft", StringComparison.OrdinalIgnoreCase);
        // Ignore only the switch changed by this card. Action, principal, trigger and all other settings remain part of the identity.
        root.Element(ns + "Settings")?.Elements(ns + "Enabled").Remove();
        string fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(document.ToString(SaveOptions.DisableFormatting))));
        return new FirewallTask(path, enabled, fingerprint, microsoft);
    }
    private static bool IsMicrosoftPath(string path) => path.Equals("\\Microsoft", StringComparison.OrdinalIgnoreCase) || path.StartsWith("\\Microsoft\\", StringComparison.OrdinalIgnoreCase);
    private static void Release(object value) { if (Marshal.IsComObject(value)) Marshal.FinalReleaseComObject(value); }
}

public static class TaskSchedulerFirewallTweak
{
    private const string Id = "processes.task-scheduler-firewall";
    private const string Prefix = Id + ".task.";
    public static TweakDefinition Build(IRestoreService restore, IFirewallTaskStore? store = null)
    {
        store ??= new WindowsFirewallTaskStore();
        var gate = new SemaphoreSlim(1, 1);
        return new TweakDefinition
        {
            Id = Id, Name = "Task Scheduler Firewall", Category = TweakCategory.Windows,
            Description = "Disables enabled third-party scheduled tasks outside the Microsoft task tree. Tasks whose registered author identifies Microsoft stay unchanged. Updaters, helpers and security utilities may stop running on schedule. Elite records each changed task and restores its original enabled state only if the definition still matches. Existing running instances are not stopped; new tasks are not blocked.",
            RiskLevel = TweakRiskLevel.High, RequiresAdministrator = true,
            AffectedSettings = new[] { "TaskScheduler:third-party:Enabled" },
            HasSavedOriginal = () => Saved().Count > 0,
            CurrentStateReader = async () =>
            {
                var tasks = await store.All();
                var saved = Saved();
                if (tasks.Any(task => !task.Microsoft && task.Enabled)) return TweakState.NotApplied;
                if (saved.Any(item => !tasks.Any(task => task.Path.Equals(item.Task.Path, StringComparison.OrdinalIgnoreCase)
                    && task.Fingerprint == item.Task.Fingerprint && !task.Microsoft && !task.Enabled))) return TweakState.NotApplied;
                return saved.Count > 0 ? TweakState.Applied : TweakState.Unsupported;
            },
            ApplyAction = () => Change(true), RestoreAction = () => Change(false)
        };
        List<(string Key, FirewallTask Task)> Saved()
        {
            return restore.GetRestorableTweakIds().Where(key => key.StartsWith(Prefix, StringComparison.Ordinal)).Select(key =>
            {
                var task = JsonSerializer.Deserialize<FirewallTask>(restore.GetBackup(key) ?? throw new IOException("Scheduled task original is missing."))
                    ?? throw new IOException("Invalid scheduled task backup.");
                if (string.IsNullOrEmpty(task.Path) || !task.Path.StartsWith('\\') || task.Microsoft || !task.Enabled
                    || task.Fingerprint == null || task.Fingerprint.Length != 64 || Key(task.Path) != key)
                    throw new IOException("Scheduled task backup identity is invalid. Backup retained.");
                return (key, task);
            }).ToList();
        }
        async Task<TweakActionResult> Change(bool apply)
        {
            await gate.WaitAsync();
            try
            {
                var saved = Saved();
                var targets = apply ? (await store.All()).Where(task => !task.Microsoft && task.Enabled).Select(task => (Key(task.Path), task)).ToList() : saved;
                if (targets.Count == 0) return TweakActionResult.Fail(apply ? "No enabled third-party scheduled tasks were found." : "No scheduled task originals were saved by Elite.");
                int done = 0; var errors = new List<string>();
                foreach (var (key, target) in targets)
                {
                    try
                    {
                        var current = await store.Read(target.Path);
                        var original = saved.FirstOrDefault(item => item.Key == key).Task;
                        if (current.Microsoft || current.Fingerprint != target.Fingerprint || (original != null && original.Fingerprint != current.Fingerprint))
                            throw new IOException("Task definition changed. Original backup retained.");
                        if (apply && original == null)
                        {
                            if (current.Enabled != target.Enabled) throw new IOException("Task state changed during review. No change made.");
                            restore.SaveBackup(key, JsonSerializer.Serialize(current));
                            original = JsonSerializer.Deserialize<FirewallTask>(restore.GetBackup(key) ?? "null");
                            if (original != current) throw new IOException("Task backup verification failed. No change made.");
                        }
                        bool enabled = !apply && original!.Enabled;
                        if (current.Enabled != enabled) await store.SetEnabled(current.Path, current.Fingerprint, enabled);
                        var after = await store.Read(current.Path);
                        if (after.Enabled != enabled || after.Fingerprint != current.Fingerprint) throw new IOException("Task state could not be verified. Original backup retained.");
                        if (!apply) restore.ClearBackup(key);
                        done++;
                    }
                    catch (Exception ex) when (ex is IOException or COMException or UnauthorizedAccessException or System.Security.SecurityException)
                    { errors.Add(target.Path + ": " + ex.Message); }
                }
                string result = done + " scheduled " + (done == 1 ? "task " : "tasks ") + (apply ? "disabled." : "restored.");
                return errors.Count == 0 ? TweakActionResult.Ok(result) : TweakActionResult.Fail(result + " Remaining originals retained. " + string.Join(" ", errors));
            }
            finally { gate.Release(); }
        }
    }
    private static string Key(string path) => Prefix + Convert.ToHexString(SHA256.HashData(Encoding.Unicode.GetBytes(path.ToUpperInvariant())));
}
