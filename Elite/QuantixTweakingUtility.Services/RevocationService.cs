using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Threading.Tasks;
using QuantixLicensing;

namespace QuantixTweakingUtility.Services;

public sealed class RevocationService : IRevocationService
{
	private const string EndpointResourceName = "QuantixTweakingUtility.RevocationEndpoint";

	private const string PublicKeyResourceName = "QuantixTweakingUtility.LicensePublicKey";

	private static readonly TimeSpan OfflineGracePeriod = TimeSpan.FromHours(24.0);

	private readonly ILoggingService _logging;

	private readonly string _endpointUrl;

	private readonly string _publicKeyPem;

	private readonly string _cachePath;

	public RevocationService(ILoggingService logging)
	{
		_logging = logging;
		_endpointUrl = LoadEndpoint();
		_publicKeyPem = LoadResource("QuantixTweakingUtility.LicensePublicKey");
		_cachePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Quantix", "revocations.cache");
	}

	public async Task<RevocationCheckResult> CheckAsync(string licenseId)
	{
		if (!RevocationToken.IsValidLicenseId(licenseId))
		{
			return new RevocationCheckResult(RevocationCheckStatus.Unavailable, "The license ID cannot be checked for revocation.");
		}
		if (string.IsNullOrWhiteSpace(_endpointUrl))
		{
			_logging.Warn("License", "Revocation endpoint is not configured in this build.");
			return new RevocationCheckResult(RevocationCheckStatus.NotConfigured, "Online revocation is not configured in this build.");
		}
		try
		{
			string token = await DownloadManifestAsync();
			RevocationCheckResult revocationCheckResult = Evaluate(token, licenseId);
			if ((uint)revocationCheckResult.Status <= 1u)
			{
				SaveCache(token);
			}
			return revocationCheckResult;
		}
		catch (Exception ex)
		{
			_logging.Warn("License", "Online revocation check failed: " + ex.Message);
			RevocationCheckResult revocationCheckResult2 = TryEvaluateFreshCache(licenseId);
			if ((object)revocationCheckResult2 != null)
			{
				return revocationCheckResult2;
			}
			return new RevocationCheckResult(RevocationCheckStatus.Unavailable, "Quantix could not contact the license server and has no recent signed cache. Connect to the internet and try again.");
		}
	}

	private async Task<string> DownloadManifestAsync()
	{
		using HttpClient client = new HttpClient
		{
			Timeout = TimeSpan.FromSeconds(10.0)
		};
		client.DefaultRequestHeaders.CacheControl = new CacheControlHeaderValue
		{
			NoCache = true,
			NoStore = true
		};
		client.DefaultRequestHeaders.UserAgent.ParseAdd("Quantix-Elite/3.4");
		char value = (_endpointUrl.Contains('?') ? '&' : '?');
		using HttpResponseMessage response = await client.GetAsync($"{_endpointUrl}{value}nonce={DateTimeOffset.UtcNow.ToUnixTimeSeconds()}");
		response.EnsureSuccessStatusCode();
		string obj = await response.Content.ReadAsStringAsync();
		if (obj.Length > 1048576)
		{
			throw new InvalidDataException("The revocation response is too large.");
		}
		return obj;
	}

	private RevocationCheckResult Evaluate(string token, string licenseId)
	{
		RevocationValidationResult revocationValidationResult = RevocationToken.Validate(token, _publicKeyPem);
		if (!revocationValidationResult.IsValid || (object)revocationValidationResult.Payload == null)
		{
			throw new InvalidDataException(revocationValidationResult.Message);
		}
		if (!RevocationToken.Contains(revocationValidationResult.Payload, licenseId))
		{
			return new RevocationCheckResult(RevocationCheckStatus.Clear, "License is not revoked.");
		}
		return new RevocationCheckResult(RevocationCheckStatus.Revoked, "This license has been revoked by Quantix. Contact the owner if you believe this is a mistake.");
	}

	private RevocationCheckResult? TryEvaluateFreshCache(string licenseId)
	{
		try
		{
			if (!File.Exists(_cachePath) || File.GetLastWriteTimeUtc(_cachePath) < DateTime.UtcNow.Subtract(OfflineGracePeriod))
			{
				return null;
			}
			return Evaluate(File.ReadAllText(_cachePath), licenseId);
		}
		catch (Exception ex)
		{
			_logging.Warn("License", "Cached revocation list is unusable: " + ex.Message);
			return null;
		}
	}

	private void SaveCache(string token)
	{
		try
		{
			Directory.CreateDirectory(Path.GetDirectoryName(_cachePath));
			string text = _cachePath + ".tmp";
			File.WriteAllText(text, token);
			File.Move(text, _cachePath, overwrite: true);
		}
		catch (Exception ex)
		{
			_logging.Warn("License", "Could not save the signed revocation cache: " + ex.Message);
		}
	}

	private static string LoadEndpoint()
	{
		string text = (from line in LoadResource("QuantixTweakingUtility.RevocationEndpoint").Split(new char[2] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
			select line.Trim()).FirstOrDefault((string line) => !line.StartsWith('#')) ?? string.Empty;
		if (string.IsNullOrWhiteSpace(text))
		{
			return string.Empty;
		}
		if (!Uri.TryCreate(text, UriKind.Absolute, out Uri result) || result.Scheme != Uri.UriSchemeHttps)
		{
			throw new InvalidOperationException("The embedded revocation endpoint must be an HTTPS URL.");
		}
		return result.AbsoluteUri;
	}

	private static string LoadResource(string name)
	{
		using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name) ?? throw new InvalidOperationException("Embedded resource " + name + " is missing.");
		using StreamReader streamReader = new StreamReader(stream);
		return streamReader.ReadToEnd();
	}
}
