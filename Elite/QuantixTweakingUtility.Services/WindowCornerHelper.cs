using System.Runtime.InteropServices;

namespace QuantixTweakingUtility.Services;

public static class WindowCornerHelper
{
	private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;

	private const int DWMWCP_ROUND = 2;

	[DllImport("dwmapi.dll")]
	private static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref int pvAttribute, int cbAttribute);

	public static void TryEnableRoundedCorners(nint windowHandle)
	{
		try
		{
			int pvAttribute = 2;
			DwmSetWindowAttribute(windowHandle, 33, ref pvAttribute, 4);
		}
		catch
		{
		}
	}
}
