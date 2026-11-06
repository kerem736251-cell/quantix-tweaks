using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using QuantixTweakingUtility.Models;

namespace QuantixTweakingUtility.Services;

public interface IDiskCleanupProfileStore
{
    IReadOnlyList<string> Categories();
    IReadOnlyCollection<int> UsedProfiles();
    bool CreateFlag(string category,int profile,int value);
    int? ReadFlag(string category,int profile);
    void RemoveFlag(string category,int profile,int expected);
}
public sealed class WindowsDiskCleanupProfileStore : IDiskCleanupProfileStore
{
    private readonly RegistryKey hive;
    private readonly string path;
    public WindowsDiskCleanupProfileStore(RegistryKey? hive = null,string path = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\VolumeCaches")
    { this.hive=hive ?? Registry.LocalMachine; this.path=path; }
    public IReadOnlyList<string> Categories()
    {
        using var key=hive.OpenSubKey(path);return key?.GetSubKeyNames() ?? Array.Empty<string>();
    }
    public IReadOnlyCollection<int> UsedProfiles()
    {
        var used=new HashSet<int>();
        foreach(string category in Categories())
        {
            using var key=hive.OpenSubKey(path+"\\"+category);
            foreach(string name in key?.GetValueNames() ?? Array.Empty<string>())
            {
                var match=Regex.Match(name,@"^StateFlags([0-9]{1,4})$",RegexOptions.IgnoreCase);
                if(match.Success)used.Add(int.Parse(match.Groups[1].Value));
            }
        }
        return used;
    }
    public bool CreateFlag(string category,int profile,int value)
    {
        string flag=Flag(profile);ValidateCategory(category);
        if(value is not (0 or 2))throw new IOException("Invalid Disk Cleanup selection flag.");
        using var key=hive.OpenSubKey(path+"\\"+category,true) ?? throw new IOException("Disk Cleanup category is no longer available.");
        if(key.GetValueNames().Contains(flag,StringComparer.OrdinalIgnoreCase))return false;
        key.SetValue(flag,value,RegistryValueKind.DWord);return true;
    }
    public int? ReadFlag(string category,int profile)
    {
        string flag=Flag(profile);ValidateCategory(category);
        using var key=hive.OpenSubKey(path+"\\"+category);
        object? value=key?.GetValue(flag);
        if(value==null)return null;
        if(key!.GetValueKind(flag)!=RegistryValueKind.DWord || value is not int number)throw new IOException("Disk Cleanup profile has an unexpected value type.");
        return number;
    }
    public void RemoveFlag(string category,int profile,int expected)
    {
        string flag=Flag(profile);ValidateCategory(category);
        using var key=hive.OpenSubKey(path+"\\"+category,true);
        if(key==null || !key.GetValueNames().Contains(flag,StringComparer.OrdinalIgnoreCase))return;
        if(key.GetValueKind(flag)!=RegistryValueKind.DWord || key.GetValue(flag) is not int value || value!=expected)
            throw new IOException("Temporary Disk Cleanup profile changed outside Elite; its changed flag was retained.");
        key.DeleteValue(flag,false);
        if(key.GetValueNames().Contains(flag,StringComparer.OrdinalIgnoreCase))throw new IOException("Temporary Disk Cleanup flag removal did not verify.");
    }
    private static string Flag(int profile) => profile is >=0 and <=9999 ? "StateFlags"+profile.ToString("D4") : throw new IOException("Invalid Disk Cleanup profile number.");
    private static void ValidateCategory(string category)
    { if(string.IsNullOrWhiteSpace(category)||category.IndexOfAny(new[]{'\\','/','\0'})>=0)throw new IOException("Invalid Disk Cleanup category."); }
}

public static class DiskCleanupWorkflow
{
    private static readonly SemaphoreSlim Gate=new(1,1);
    private static readonly HashSet<string> Allowed=new(StringComparer.OrdinalIgnoreCase)
    { "Temporary Files","Temporary Setup Files","Thumbnail Cache","D3D Shader Cache","Downloaded Program Files" };
    public static async Task<int> Run(IDiskCleanupProfileStore store,Func<int,Task> execute)
    {
        await Gate.WaitAsync();
        try
        {
            var categories=store.Categories();
            int selected=categories.Count(Allowed.Contains);
            if(selected==0)throw new IOException("No supported Windows Disk Cleanup cache categories are available.");
            var used=store.UsedProfiles().ToHashSet();int profile=9999;
            while(profile>=0 && used.Contains(profile))profile--;
            if(profile<0)throw new IOException("No unused Disk Cleanup profile is available. Existing profiles were left unchanged.");
            var owned=new List<(string Category,int Value)>();
            Exception? operationError=null;var cleanupErrors=new List<string>();
            try
            {
                foreach(string category in categories)
                {
                    int value=Allowed.Contains(category)?2:0;
                    if(!store.CreateFlag(category,profile,value))throw new IOException("Disk Cleanup profile was claimed by another operation. Cleanup was not started.");
                    owned.Add((category,value));
                    if(store.ReadFlag(category,profile)!=value)throw new IOException("Disk Cleanup profile verification failed. Cleanup was not started.");
                }
                foreach(string category in store.Categories())
                {
                    int? flag=store.ReadFlag(category,profile);
                    var expected=owned.SingleOrDefault(item=>item.Category.Equals(category,StringComparison.OrdinalIgnoreCase));
                    if(expected.Category!=null ? flag!=expected.Value : flag.HasValue)throw new IOException("Disk Cleanup profile changed before execution. Cleanup was not started.");
                }
                await execute(profile);
            }
            catch(Exception ex){operationError=ex;}
            finally
            {
                foreach(var item in owned)
                    try{store.RemoveFlag(item.Category,profile,item.Value);}
                    catch(Exception ex){cleanupErrors.Add(ex.Message);}
            }
            string cleanupMessage=cleanupErrors.Count==0?"":" Temporary profile cleanup: "+string.Join(" ",cleanupErrors.Distinct());
            if(operationError!=null)throw new IOException("Windows Disk Cleanup did not complete. "+operationError.Message+cleanupMessage,operationError);
            if(cleanupErrors.Count>0)throw new IOException("Windows Disk Cleanup finished, but its temporary profile was not fully removed."+cleanupMessage);
            return selected;
        }
        finally{Gate.Release();}
    }
}

public interface IWindowsDiskCleanupRunner { bool Available { get; } Task<int> Run(); }
public sealed class WindowsDiskCleanupRunner : IWindowsDiskCleanupRunner
{
    private static string Executable=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"cleanmgr.exe");
    public bool Available=>File.Exists(Executable);
    public Task<int> Run()=>DiskCleanupWorkflow.Run(new WindowsDiskCleanupProfileStore(),async profile=>
    {
        var command=new ProcessStartInfo(Executable){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden};
        command.ArgumentList.Add("/sagerun:"+profile);
        using var process=Process.Start(command) ?? throw new IOException("Windows Disk Cleanup could not start.");
        await process.WaitForExitAsync();
        if(process.ExitCode!=0)throw new IOException("Windows Disk Cleanup returned exit code "+process.ExitCode+".");
    });
}

public static class WindowsDiskCleanupTweak
{
    public static TweakDefinition Build(IWindowsDiskCleanupRunner? runner=null)
    {
        runner??=new WindowsDiskCleanupRunner();
        return new TweakDefinition
        {
            Id="windows.cleanup.disk-cleanup",Name="Windows Cleaner",Category=TweakCategory.Windows,
            Description="Runs built-in Windows Disk Cleanup for available temporary files, temporary setup files, thumbnails, DirectX shader cache and downloaded ActiveX/program cache. Downloads, Recycle Bin, previous Windows installations, driver packages and other categories are excluded. Uses an unused temporary cleanup profile and removes its flags afterward. Deleted cache files cannot be restored by Elite.",
            RiskLevel=TweakRiskLevel.Medium,RequiresAdministrator=true,IsOneTimeAction=true,
            AffectedSettings=new[]{"action:cleanup:windows-disk-cleanup"},
            CurrentStateReader=()=>Task.FromResult(runner.Available?TweakState.NotApplied:TweakState.Unsupported),
            RestoreAction=()=>Task.FromResult(TweakActionResult.Fail("Windows Disk Cleanup deletes cache files; it has no saved setting to restore.")),
            ApplyAction=async()=>
            {
                if(!runner.Available)return TweakActionResult.Fail("Windows Disk Cleanup is not installed on this Windows system.");
                int count=await runner.Run();return TweakActionResult.Ok("Windows Disk Cleanup finished for "+count+" cache categories. Windows may retain locked files.");
            }
        };
    }
}
