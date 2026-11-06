using QuantixTweakingUtility.Models;

namespace QuantixTweakingUtility.Services;

public interface IAppSettingsService
{
	AppSettings Settings { get; }

	bool IsLaunchAtSignInEnabled();

	void SetLaunchAtSignIn(bool enabled);

	void Save();
}
