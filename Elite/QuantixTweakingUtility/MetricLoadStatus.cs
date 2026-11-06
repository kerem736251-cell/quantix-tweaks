using System;

namespace QuantixTweakingUtility;

public static class MetricLoadStatus
{
    public static string Describe(double? utilization) => !utilization.HasValue || !double.IsFinite(utilization.Value)
        || utilization.Value < 0 || utilization.Value > 1 ? "UNAVAILABLE"
        : utilization.Value >= 0.9 ? "HIGH LOAD" : "NOMINAL";
}
