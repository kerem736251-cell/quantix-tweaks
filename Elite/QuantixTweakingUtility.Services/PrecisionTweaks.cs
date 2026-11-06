using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using QuantixTweakingUtility.Models;

namespace QuantixTweakingUtility.Services;

public static class PrecisionTweaks
{
	public static TweakDefinition BuildAcceleration(IRestoreService restore)
	{
		TweakDefinition tweakDefinition = new TweakDefinition();
		tweakDefinition.Id = "gaming.mouse-acceleration.disable";
		tweakDefinition.Name = "Precision · Disable Pointer Acceleration";
		tweakDefinition.Description = "Disables Windows Enhance pointer precision immediately for a consistent pointer response. Does not change DPI, in-game sensitivity or games that use raw mouse input.";
		tweakDefinition.Category = TweakCategory.Input;
		tweakDefinition.RiskLevel = TweakRiskLevel.Low;
		tweakDefinition.AffectedSettings = new string[3] { "HKCU\\Mouse:MouseSpeed", "HKCU\\Mouse:MouseThreshold1", "HKCU\\Mouse:MouseThreshold2" };
		tweakDefinition.CurrentStateReader = delegate
		{
			int[] array = new int[3];
			return Task.FromResult(MouseValues(3u, 0u, array, 0u) ? ((array[2] == 0) ? TweakState.Applied : TweakState.NotApplied) : TweakState.Unknown);
		};
		tweakDefinition.ApplyAction = delegate
		{
			int[] array = new int[3];
			if (!MouseValues(3u, 0u, array, 0u))
			{
				return Task.FromResult(TweakActionResult.Fail("Cannot read mouse acceleration. Nothing changed."));
			}
			if (restore.GetBackup("gaming.mouse-acceleration.disable") == null)
			{
				restore.SaveBackup("gaming.mouse-acceleration.disable", $"{array[2]}|{array[0]}|{array[1]}");
			}
			return Task.FromResult(MouseValues(4u, 0u, new int[3], 3u) ? TweakActionResult.Ok("Mouse acceleration disabled immediately.") : TweakActionResult.Fail("Windows rejected the change. The backup is retained."));
		};
		tweakDefinition.RestoreAction = delegate
		{
			string[] array = restore.GetBackup("gaming.mouse-acceleration.disable")?.Split('|');
			if (array == null || array.Length != 3 || !int.TryParse(array[0], out var result) || !int.TryParse(array[1], out var result2) || !int.TryParse(array[2], out var result3))
			{
				return Task.FromResult(TweakActionResult.Fail("No valid Quantix mouse backup exists. Nothing changed."));
			}
			if (!MouseValues(4u, 0u, new int[3] { result2, result3, result }, 3u))
			{
				return Task.FromResult(TweakActionResult.Fail("Windows could not restore mouse acceleration."));
			}
			restore.ClearBackup("gaming.mouse-acceleration.disable");
			return Task.FromResult(TweakActionResult.Ok("Your previous mouse acceleration has been restored."));
		};
		return tweakDefinition;
	}

	[DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool MouseValues(uint action, uint parameter, [In][Out] int[] values, uint flags);

	public static IEnumerable<TweakDefinition> Build(IRestoreService restore)
	{
		yield return Create(restore, "precision.pointer-speed", "Precision · Standard Pointer Speed", "Sets Windows pointer speed to its standard value (10 / classic 6 of 11). This changes desktop sensitivity, not mouse DPI or raw-input aiming.", 112u, 113u, 10u, usePointerValue: true, "MouseSensitivity");
		yield return Create(restore, "precision.pointer-trails", "Precision · Disable Pointer Trails", "Removes the trailing cursor effect for a cleaner desktop pointer. This is a visibility preference, not an FPS or input-latency boost.", 94u, 93u, 0u, usePointerValue: false, "MouseTrails");
		yield return Create(restore, "precision.snap-to", "Precision · Disable Pointer Snap", "Stops Windows moving your pointer automatically to a dialog's default button. Your previous setting is saved for restore.", 95u, 96u, 0u, usePointerValue: false, "SnapToDefaultButton");
	}

	private static TweakDefinition Create(IRestoreService restore, string id, string name, string description, uint getAction, uint setAction, uint target, bool usePointerValue, string setting)
	{
		TweakDefinition tweakDefinition = new TweakDefinition();
		tweakDefinition.Id = id;
		tweakDefinition.Name = name;
		tweakDefinition.Description = description;
		tweakDefinition.Category = TweakCategory.Input;
		tweakDefinition.RiskLevel = TweakRiskLevel.Medium;
		tweakDefinition.AffectedSettings = new string[1] { "HKCU\\Control Panel\\Mouse:" + setting };
		tweakDefinition.CurrentStateReader = () => Task.FromResult(GetValue(getAction, 0u, out var value, 0u) ? (((getAction == 94) ? (value <= 1) : (value == target)) ? TweakState.Applied : TweakState.NotApplied) : TweakState.Unknown);
		tweakDefinition.ApplyAction = delegate
		{
			if (!GetValue(getAction, 0u, out var value, 0u))
			{
				return Task.FromResult(TweakActionResult.Fail("Windows could not read the current pointer setting. Nothing changed."));
			}
			if (restore.GetBackup(id) == null)
			{
				restore.SaveBackup(id, value.ToString(CultureInfo.InvariantCulture));
			}
			return Task.FromResult(Set(setAction, target, usePointerValue) ? TweakActionResult.Ok("Pointer setting applied immediately. Your previous value is saved.") : TweakActionResult.Fail("Windows rejected the pointer change. The backup is retained."));
		};
		tweakDefinition.RestoreAction = delegate
		{
			if (!uint.TryParse(restore.GetBackup(id), NumberStyles.Integer, CultureInfo.InvariantCulture, out var result))
			{
				return Task.FromResult(TweakActionResult.Fail("No Quantix backup exists for this setting. Nothing changed."));
			}
			if (!Set(setAction, result, usePointerValue))
			{
				return Task.FromResult(TweakActionResult.Fail("Windows could not restore the setting. The backup is retained."));
			}
			restore.ClearBackup(id);
			return Task.FromResult(TweakActionResult.Ok("Your previous pointer setting has been restored."));
		};
		return tweakDefinition;
	}

	private static bool Set(uint action, uint value, bool usePointerValue)
	{
		return SetValue(action, (!usePointerValue) ? value : 0u, usePointerValue ? new IntPtr(value) : IntPtr.Zero, 3u);
	}

	[DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool GetValue(uint action, uint parameter, out uint value, uint flags);

	[DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool SetValue(uint action, uint parameter, nint value, uint flags);
}
