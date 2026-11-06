using System;

namespace QuantixTweakingUtility.Services;

public static class BatchOperationOrder
{
    public const string PowerPlan = "cpu.power-plan.quantix-elite";
    public static int Priority(string id, bool apply) => apply && id == "network.stack.reset" ? -2 : id == PowerPlan ? (apply ? -1 : 1) : 0;
    public static bool IsPlanSpecific(string id) => id.StartsWith("power.policy.",StringComparison.Ordinal)
        || id is "perf.usb-selective-suspend" or "power.pcie-aspm.disable" or WifiPowerTweak.Id;
}
