using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using QuantixTweakingUtility.Models;

namespace QuantixTweakingUtility.Services;

public enum NetworkResetStep { FlushDns, Winsock, TcpIp }
public record NetworkResetResult(int ExitCode, string Report);
public interface INetworkResetRunner
{
    bool Available { get; }
    Task<NetworkResetResult> Run(NetworkResetStep step);
}
public sealed class WindowsNetworkResetRunner : INetworkResetRunner
{
    private readonly ILoggingService? log;
    public WindowsNetworkResetRunner(ILoggingService? log = null) { this.log = log; }
    public bool Available => File.Exists(Path.Combine(Environment.SystemDirectory,"netsh.exe")) && File.Exists(Path.Combine(Environment.SystemDirectory,"ipconfig.exe"));
    public static ProcessStartInfo Command(NetworkResetStep step, string systemDirectory, string resetLog)
    {
        string[] arguments = step switch {
            NetworkResetStep.FlushDns => new[]{"/flushdns"},
            NetworkResetStep.Winsock => new[]{"winsock","reset"},
            NetworkResetStep.TcpIp => new[]{"int","ip","reset",resetLog},
            _ => throw new IOException("Unknown network reset operation.")
        };
        var command=new ProcessStartInfo(Path.Combine(systemDirectory,step==NetworkResetStep.FlushDns?"ipconfig.exe":"netsh.exe")){
            UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,RedirectStandardOutput=true,RedirectStandardError=true
        };
        foreach(string argument in arguments)command.ArgumentList.Add(argument);
        return command;
    }
    public async Task<NetworkResetResult> Run(NetworkResetStep step)
    {
        string directory=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Quantix","Logs");
        Directory.CreateDirectory(directory);
        string resetLog=Path.Combine(directory,"tcpip-reset-"+Guid.NewGuid().ToString("N")+".log");
        using var process=Process.Start(Command(step,Environment.SystemDirectory,resetLog)) ?? throw new IOException("Windows network reset command could not start.");
        var output=process.StandardOutput.ReadToEndAsync();var error=process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        string report=(await output).Trim()+Environment.NewLine+(await error).Trim();
        log?.Info("NetworkReset",step+" exit "+process.ExitCode+". "+report+(step==NetworkResetStep.TcpIp?" TCP/IP report: "+resetLog:""));
        return new(process.ExitCode,report.Trim());
    }
}
public static class NetworkResetTweak
{
    public static TweakDefinition Build(INetworkResetRunner? runner=null)
    {
        runner??=new WindowsNetworkResetRunner();var gate=new SemaphoreSlim(1,1);
        return new TweakDefinition{
            Id="network.stack.reset",Name="Reset all network settings",Category=TweakCategory.Network,
            Description="Flushes the DNS resolver cache, resets the Winsock catalog, and asks Windows to reset TCP/IP stack configuration. Applies across the Windows network stack, not just one adapter. This can remove custom IP/DNS configuration and networking extensions, interrupt connections, and require VPN/security software reconfiguration. Record any custom network settings first. Restart Windows afterward; Elite does not restart automatically. This is a one-time troubleshooting action with no automatic restore. It does not uninstall/reinstall adapters or reset every Windows networking feature.",
            RiskLevel=TweakRiskLevel.High,RequiresAdministrator=true,RequiresRestart=true,IsOneTimeAction=true,
            AffectedSettings=new[]{"action:network:flush-dns-winsock-tcpip-reset"},
            CurrentStateReader=()=>Task.FromResult(runner.Available?TweakState.NotApplied:TweakState.Unsupported),
            RestoreAction=()=>Task.FromResult(TweakActionResult.Fail("Network stack reset is a one-time troubleshooting operation. Elite cannot reconstruct the previous stack configuration; re-enter custom settings manually.")),
            ApplyAction=async()=>{
                await gate.WaitAsync();
                try{
                    if(!runner.Available)return TweakActionResult.Fail("Required Windows network reset tools are unavailable.");
                    var completed=new List<string>();
                    foreach(var step in new[]{NetworkResetStep.FlushDns,NetworkResetStep.Winsock,NetworkResetStep.TcpIp}){
                        try{
                            var result=await runner.Run(step);
                            if(result.ExitCode!=0)throw new IOException("Windows returned exit code "+result.ExitCode+". "+result.Report);
                            completed.Add(step.ToString());
                        }catch(Exception ex) when(ex is IOException or System.ComponentModel.Win32Exception){
                            return TweakActionResult.Fail("Network reset stopped at "+step+". Commands already completed: "+(completed.Count==0?"none":string.Join(", ",completed))+". Earlier changes were not rolled back. Restart may still be required. "+ex.Message);
                        }
                    }
                    return TweakActionResult.Ok("DNS, Winsock and TCP/IP reset commands returned success. Restart Windows to finish, then check connectivity and the Windows reset report for any per-item failures. Custom network settings may need to be re-entered.");
                }finally{gate.Release();}
            }
        };
    }
}
