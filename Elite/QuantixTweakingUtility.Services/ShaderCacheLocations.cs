using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace QuantixTweakingUtility.Services;

public static class ShaderCacheLocations
{
    public static IReadOnlyList<string> Resolve(string localData, string commonData)
    {
        var paths = new List<string>();
        foreach (string relative in new[] { "D3DSCache", @"NVIDIA\DXCache", @"NVIDIA\GLCache", @"AMD\DxCache", @"AMD\DxcCache", @"AMD\GLCache", @"AMD\VkCache" })
            paths.Add(Path.GetFullPath(Path.Combine(localData,relative)));
        paths.Add(Path.GetFullPath(Path.Combine(commonData,@"NVIDIA Corporation\NV_Cache")));
        string low = Path.Combine(Directory.GetParent(localData)!.FullName,"LocalLow","NVIDIA","PerDriverVersion");
        try
        {
            if(Directory.Exists(low) && (File.GetAttributes(low)&FileAttributes.ReparsePoint)==0)
                foreach(string version in Directory.GetDirectories(low))
                    foreach(string leaf in new[]{"DXCache","GLCache"}) paths.Add(Path.GetFullPath(Path.Combine(version,leaf)));
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException) { }
        return paths.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }
}
