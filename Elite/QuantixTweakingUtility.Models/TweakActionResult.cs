namespace QuantixTweakingUtility.Models;

public sealed class TweakActionResult
{
	public bool Success { get; init; }

	public string Message { get; init; } = string.Empty;

	public static TweakActionResult Ok(string message = "Done.")
	{
		return new TweakActionResult
		{
			Success = true,
			Message = message
		};
	}

	public static TweakActionResult Fail(string message)
	{
		return new TweakActionResult
		{
			Success = false,
			Message = message
		};
	}
}
