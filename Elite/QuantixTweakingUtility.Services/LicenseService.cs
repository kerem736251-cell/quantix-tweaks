using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using QuantixLicensing;

namespace QuantixTweakingUtility.Services;

public sealed class LicenseService : ILicenseService
{
	private readonly ILoggingService _logging;

	private readonly string _publicKey;

	private readonly Lazy<string> _hardwareId = new Lazy<string>(HardwareFingerprint.GetCurrentHash);

	public string HardwareId => _hardwareId.Value;

	public string FormattedHardwareId => HardwareFingerprint.FormatHash(HardwareId);

	public string LicenseFilePath { get; } = ActivationStore.PathFor("quantix-elite");

	public LicenseService(ILoggingService logging, IRevocationService revocations)
	{
		_logging = logging;
		using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("QuantixTweakingUtility.LicensePublicKey") ?? throw new InvalidOperationException("License verifier is missing.");
		using StreamReader streamReader = new StreamReader(stream);
		_publicKey = streamReader.ReadToEnd();
	}

	public string? ReadSavedToken()
	{
		try
		{
			return ActivationStore.Read(LicenseFilePath);
		}
		catch (Exception ex)
		{
			_logging.Error("License", "Could not read saved activation.", ex);
			return null;
		}
	}

	public Task<LicenseValidationResult> ValidateSavedLicenseAsync()
	{
		return Task.FromResult(ValidateToken(ReadSavedToken() ?? ""));
	}

	public LicenseValidationResult ValidateToken(string token, string? hwidOverride = null, DateTimeOffset? nowUtc = null)
	{
		try
		{
			return LicenseToken.Validate(token, _publicKey, hwidOverride ?? HardwareId, nowUtc);
		}
		catch (Exception ex)
		{
			_logging.Error("License", "Device identity could not be read.", ex);
			return new LicenseValidationResult(IsValid: false, LicenseStatus.ConfigurationError, ex.Message);
		}
	}

	public Task<LicenseValidationResult> ValidateTokenOnlineAsync(string token)
	{
		return Task.FromResult(ValidateToken(token));
	}

	public Task<LicenseValidationResult> ActivateAsync(string token)
	{
		LicenseValidationResult licenseValidationResult = ValidateToken(token);
		if (!licenseValidationResult.IsValid)
		{
			return Task.FromResult(licenseValidationResult);
		}
		try
		{
			ActivationStore.Save(LicenseFilePath, token);
			_logging.Info("License", "Saved activation " + licenseValidationResult.License.LicenseId);
		}
		catch (Exception ex)
		{
			_logging.Error("License", "Could not save activation.", ex);
			licenseValidationResult = new LicenseValidationResult(IsValid: false, LicenseStatus.ConfigurationError, "The key is valid, but could not be saved. Check folder access and try again.");
		}
		return Task.FromResult(licenseValidationResult);
	}
}
