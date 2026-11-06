using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using QuantixTweakingUtility.Models;

namespace QuantixTweakingUtility.Services;

internal static class NvidiaCardTweaks
{
    public static IEnumerable<TweakDefinition> Build()
    {
        var service = new NvidiaSettingsService();
        foreach (var option in NvidiaSettingsService.CardOptions)
        {
            yield return new TweakDefinition
            {
                Id = $"gpu.nvidia.native.{option.Id:x8}",
                Name = option.Title,
                Description = option.Detail,
                Category = TweakCategory.Gpu,
                RiskLevel = option.Id == NvidiaSettingsService.MpoId ? TweakRiskLevel.High : TweakRiskLevel.Medium,
                RequiresAdministrator = true,
                RequiresRestart = option.Id == NvidiaSettingsService.MpoId,
                HasSavedOriginal = () => service.HasOriginal(option.Id),
                AffectedSettings = new[] { $"NVIDIA-DRS:{option.Id:x8}" },
                CurrentStateReader = () =>
                {
                    try
                    {
                        return Task.FromResult(service.Read(option.Id).Value == option.Target
                            ? TweakState.Applied : TweakState.NotApplied);
                    }
                    catch (IOException) { return Task.FromResult(TweakState.Unsupported); }
                    catch (DllNotFoundException) { return Task.FromResult(TweakState.Unsupported); }
                    catch (EntryPointNotFoundException) { return Task.FromResult(TweakState.Unsupported); }
                },
                ApplyAction = () =>
                {
                    service.Change(option.Id, true);
                    return Task.FromResult(TweakActionResult.Ok(option.Title + " applied."));
                },
                RestoreAction = () =>
                {
                    service.Change(option.Id, false);
                    return Task.FromResult(TweakActionResult.Ok(option.Title + " restored."));
                }
            };
        }
    }
}
