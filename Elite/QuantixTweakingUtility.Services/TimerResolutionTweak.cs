using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using QuantixTweakingUtility.Models;

namespace QuantixTweakingUtility.Services;

public record TimerPowerPolicy(uint Control,uint State);
public interface ITimerResolutionBackend
{
    bool SupportsOneMillisecond { get; }
    TimerPowerPolicy? ReadPower();
    void WritePower(TimerPowerPolicy value);
    void Begin();
    void End();
}
public sealed class WindowsTimerResolutionBackend : ITimerResolutionBackend
{
    public bool SupportsOneMillisecond => timeGetDevCaps(out var caps,8)==0 && caps.Minimum<=1 && caps.Maximum>=1;
    public TimerPowerPolicy? ReadPower()
    {
        if(Environment.OSVersion.Version.Build<22000)return null;
        var state=new PowerState {Version=1};
        if(!GetProcessInformation(new IntPtr(-1),4,ref state,12))throw new Win32Exception(Marshal.GetLastWin32Error());
        return new(state.Control,state.State);
    }
    public void WritePower(TimerPowerPolicy value)
    {
        var state=new PowerState{Version=1,Control=value.Control,State=value.State};
        if(!SetProcessInformation(new IntPtr(-1),4,ref state,12))throw new Win32Exception(Marshal.GetLastWin32Error());
    }
    public void Begin(){if(timeBeginPeriod(1)!=0)throw new IOException("Windows rejected the 1ms timer request.");}
    public void End(){if(timeEndPeriod(1)!=0)throw new IOException("Windows rejected release of the 1ms timer request; retry Restore.");}
    [StructLayout(LayoutKind.Sequential)] private struct Caps { public uint Minimum,Maximum; }
    [StructLayout(LayoutKind.Sequential)] private struct PowerState { public uint Version,Control,State; }
    [DllImport("winmm.dll")]private static extern uint timeGetDevCaps(out Caps caps,uint size);
    [DllImport("winmm.dll")]private static extern uint timeBeginPeriod(uint period);
    [DllImport("winmm.dll")]private static extern uint timeEndPeriod(uint period);
    [DllImport("kernel32.dll",SetLastError=true)]
    [return:MarshalAs(UnmanagedType.Bool)]private static extern bool GetProcessInformation(IntPtr process,int information,ref PowerState state,uint size);
    [DllImport("kernel32.dll",SetLastError=true)]
    [return:MarshalAs(UnmanagedType.Bool)]private static extern bool SetProcessInformation(IntPtr process,int information,ref PowerState state,uint size);
}

public sealed class TimerResolutionLease : IDisposable
{
    public static TimerResolutionLease Shared { get; }=new(new WindowsTimerResolutionBackend());
    private const uint TimerMask=4;
    private readonly ITimerResolutionBackend backend;
    private readonly object gate=new();
    private TimerPowerPolicy? original;
    private bool requested,disposed;
    public TimerResolutionLease(ITimerResolutionBackend backend){this.backend=backend;}
    public bool Supported=>backend.SupportsOneMillisecond;
    public bool Active{get{lock(gate)return requested;}}
    public bool HasOriginal{get{lock(gate)return requested||original!=null;}}
    public void Enable()
    {
        lock(gate)
        {
            ObjectDisposedException.ThrowIf(disposed,this);
            if(requested)return;
            if(!Supported)throw new IOException("A 1ms timer period is not supported by this Windows timer service.");
            original??=backend.ReadPower();
            try
            {
                if(original!=null)
                {
                    var current=backend.ReadPower() ?? throw new IOException("The process timer policy became unavailable.");
                    backend.WritePower(new(current.Control|TimerMask,current.State&~TimerMask));
                    var after=backend.ReadPower();
                    if(after==null||(after.Control&TimerMask)==0||(after.State&TimerMask)!=0)throw new IOException("Windows did not retain the timer-request policy.");
                }
                backend.Begin();requested=true;
            }
            catch(Exception operation)
            {
                try{RestorePower();}
                catch(Exception recovery){throw new IOException(operation.Message+" Timer policy recovery is pending: "+recovery.Message,operation);}
                throw;
            }
        }
    }
    public void Disable()
    {
        lock(gate)
        {
            if(requested){backend.End();requested=false;}
            RestorePower();
        }
    }
    private void RestorePower()
    {
        if(original==null)return;
        var current=backend.ReadPower() ?? throw new IOException("The original process timer policy could not be recovered.");
        var desired=new TimerPowerPolicy((current.Control&~TimerMask)|(original.Control&TimerMask),(current.State&~TimerMask)|(original.State&TimerMask));
        backend.WritePower(desired);
        var after=backend.ReadPower();
        if(after==null||(after.Control&TimerMask)!=(original.Control&TimerMask)||(after.State&TimerMask)!=(original.State&TimerMask))
            throw new IOException("Process timer policy restoration did not verify. Retry Restore.");
        original=null;
    }
    public void Dispose(){lock(gate){if(disposed)return;Disable();disposed=true;}}
}

public static class TimerResolutionTweak
{
    public static TweakDefinition Build(TimerResolutionLease? lease=null)
    {
        lease??=TimerResolutionLease.Shared;
        return new TweakDefinition
        {
            Id="windows.timer-resolution.1ms",Name="Timer Resolution 1ms",Category=TweakCategory.Windows,RiskLevel=TweakRiskLevel.High,
            Description="Requests a 1ms multimedia timer period while Elite is open, including when minimized on Windows 11. Since Windows 10 version 2004 this documented API is process-scoped: it does not force other games or applications to use 1ms timers. Can increase wakeups and power use. Restore or closing Elite releases its request and restores its process timer policy; other applications' timer requests remain independent.",
            AffectedSettings=new[]{"process:Elite:multimedia-timer-period"},HasSavedOriginal=()=>lease.HasOriginal,
            CurrentStateReader=()=>Task.FromResult(!lease.Supported?TweakState.Unsupported:lease.Active?TweakState.Applied:TweakState.NotApplied),
            ApplyAction=()=>{lease.Enable();return Task.FromResult(TweakActionResult.Ok("Elite's 1ms timer request is active until Restore or exit. This does not force timers in other processes."));},
            RestoreAction=()=>{lease.Disable();return Task.FromResult(TweakActionResult.Ok("Elite's timer request released and its original process timer policy restored."));}
        };
    }
}
