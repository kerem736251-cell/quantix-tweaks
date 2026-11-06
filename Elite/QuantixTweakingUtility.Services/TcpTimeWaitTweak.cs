using Microsoft.Win32;
using QuantixTweakingUtility.Models;

namespace QuantixTweakingUtility.Services;

public static class TcpTimeWaitTweak
{
    public static TweakDefinition Build(IRestoreService restore, RegistryKey? hive = null,
        string path = @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters") => RegistryDwordTweaks.Build(
            "network.tcp.time-wait.30", "TcpTimedWaitDelay",
            "Sets TCP TIME_WAIT retention to 30 seconds. This can release closed-connection resources sooner under high connection churn; it does not shorten an active connection's latency or affect UDP game traffic. A shorter retention interval changes TCP connection-reuse behavior. Elite preserves the original DWORD or its absence for restore. Restart Windows after changing this setting.",
            TweakCategory.Network, TweakRiskLevel.Medium, hive ?? Registry.LocalMachine,
            @"HKLM\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters:TcpTimedWaitDelay", path, "TcpTimedWaitDelay", 30, restore,
            administrator: true, restart: true);
}
