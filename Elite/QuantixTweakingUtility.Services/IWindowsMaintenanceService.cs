using System.Threading.Tasks;

namespace QuantixTweakingUtility.Services;

public interface IWindowsMaintenanceService
{
	Task<(bool Success, string Message)> VerifySystemFilesAsync();
}
