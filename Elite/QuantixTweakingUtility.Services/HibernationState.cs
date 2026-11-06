using System.Runtime.InteropServices;

namespace QuantixTweakingUtility.Services;

public static class HibernationState
{
    // SYSTEM_POWER_CAPABILITIES: eight one-byte BOOLEAN fields precede HiberFilePresent.
    // Reserve the complete 76-byte structure, including fields not used by this query.
    [StructLayout(LayoutKind.Explicit, Size = 76)]
    private struct PowerCapabilities
    {
        [FieldOffset(8)] public byte HiberFilePresent;
    }

    [DllImport("powrprof.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool GetPwrCapabilities(out PowerCapabilities capabilities);

    public static bool? ReadEnabled() => GetPwrCapabilities(out var capabilities)
        ? capabilities.HiberFilePresent != 0 : null;
}
