using System;
using System.Threading.Tasks;
using QuantixLicensing;

namespace QuantixTweakingUtility.Services;

public interface ILicenseService
{
	string HardwareId { get; }

	string FormattedHardwareId { get; }

	string LicenseFilePath { get; }

	string? ReadSavedToken();

	Task<LicenseValidationResult> ValidateSavedLicenseAsync();

	LicenseValidationResult ValidateToken(string token, string? hwidOverride = null, DateTimeOffset? nowUtc = null);

	Task<LicenseValidationResult> ValidateTokenOnlineAsync(string token);

	Task<LicenseValidationResult> ActivateAsync(string token);
}
