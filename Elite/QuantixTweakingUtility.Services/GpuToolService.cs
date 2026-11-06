using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace QuantixTweakingUtility.Services;

public sealed class GpuToolService : IGpuToolService
{
	private sealed record ReleaseAsset(string Version, string DownloadUrl, string Sha256);

	private sealed class GitHubRelease
	{
		[JsonPropertyName("tag_name")]
		public string? TagName { get; init; }

		[JsonPropertyName("prerelease")]
		public bool Prerelease { get; init; }

		[JsonPropertyName("draft")]
		public bool Draft { get; init; }

		[JsonPropertyName("assets")]
		public List<GitHubAsset>? Assets { get; init; }
	}

	private sealed class GitHubAsset
	{
		[JsonPropertyName("name")]
		public string? Name { get; init; }

		[JsonPropertyName("browser_download_url")]
		public string? BrowserDownloadUrl { get; init; }

		[JsonPropertyName("digest")]
		public string? Digest { get; init; }
	}

	private const string LatestReleaseApi = "https://api.github.com/repos/Orbmu2k/nvidiaProfileInspector/releases/latest";

	private const string ExpectedAssetName = "nvidiaProfileInspector.zip";

	private const string ProfileResourceName = "QuantixTweakingUtility.QuantixNvidiaProfile";

	private const long MaximumArchiveBytes = 26214400L;

	private const long MaximumExtractedBytes = 104857600L;

	private static readonly ReleaseAsset FallbackRelease = new ReleaseAsset("v3.0.2.1", "https://github.com/Orbmu2k/nvidiaProfileInspector/releases/download/v3.0.2.1/nvidiaProfileInspector.zip", "88dcf3514111e8de630688467c03c36d8c2a8ad9ebc8073f27c069f82b75bb40");

	private readonly ILoggingService _logging;

	public GpuToolService(ILoggingService logging)
	{
		_logging = logging;
	}

	public async Task<GpuToolResult> PrepareNvidiaProfileInspectorAsync(bool openInspector = true)
	{
		string temporaryArchive = null;
		string stagingDirectory = null;
		try
		{
			using HttpClient client = CreateHttpClient();
			ReleaseAsset release = await ResolveReleaseAsync(client);
			ValidateRelease(release);
			string toolsRoot = Path.GetFullPath(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Quantix", "Tools", "NvidiaProfileInspector"));
			Directory.CreateDirectory(toolsRoot);
			string path = string.Concat(release.Version.Select(delegate(char character)
			{
				bool flag = char.IsLetterOrDigit(character);
				if (!flag)
				{
					flag = ((character == '-' || character == '.' || character == '_') ? true : false);
				}
				return flag ? character : '_';
			}));
			string versionDirectory = Path.GetFullPath(Path.Combine(toolsRoot, path));
			EnsureChildPath(toolsRoot, versionDirectory);
			string executablePath = Path.Combine(versionDirectory, "nvidiaProfileInspector.exe");
			string markerPath = Path.Combine(versionDirectory, "verified-sha256.txt");
			temporaryArchive = Path.Combine(Path.GetTempPath(), $"quantix-nvpi-{Guid.NewGuid():N}.zip");
			await DownloadArchiveAsync(client, release.DownloadUrl, temporaryArchive);
			if (!string.Equals(await CalculateSha256Async(temporaryArchive), release.Sha256, StringComparison.OrdinalIgnoreCase))
			{
				throw new InvalidDataException("The NVIDIA Profile Inspector archive failed SHA-256 verification.");
			}
			stagingDirectory = Path.Combine(toolsRoot, $".staging-{Guid.NewGuid():N}");
			Directory.CreateDirectory(stagingDirectory);
			ExtractVerifiedArchive(temporaryArchive, stagingDirectory);
			string? path2 = Directory.EnumerateFiles(stagingDirectory, "nvidiaProfileInspector.exe", SearchOption.AllDirectories).SingleOrDefault() ?? throw new InvalidDataException("The verified archive does not contain nvidiaProfileInspector.exe.");
			if (Directory.Exists(versionDirectory))
			{
				DeleteDirectoryWithinRoot(toolsRoot, versionDirectory);
			}
			Directory.CreateDirectory(versionDirectory);
			foreach (string item in Directory.EnumerateFiles(Path.GetDirectoryName(path2), "*", SearchOption.TopDirectoryOnly))
			{
				File.Copy(item, Path.Combine(versionDirectory, Path.GetFileName(item)), overwrite: true);
			}
			await File.WriteAllTextAsync(markerPath, release.Sha256);
			string text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Quantix", "Profiles");
			Directory.CreateDirectory(text);
			string profilePath = Path.Combine(text, "quantix.nip");
			await WriteEmbeddedProfileAsync(profilePath);
			if (openInspector)
			{
				Process.Start(new ProcessStartInfo
				{
					FileName = executablePath,
					UseShellExecute = false,
					WorkingDirectory = versionDirectory,
					ArgumentList = { "-mergeImport", profilePath }
				});
			}
			_logging.Info("GpuTools", openInspector ? ("Opened NVIDIA Profile Inspector " + release.Version + " with the verified Quantix merge profile.") : ("Verified NVIDIA Profile Inspector " + release.Version + " and the embedded Quantix profile."));
			return new GpuToolResult(Success: true, openInspector ? ("NVIDIA Profile Inspector " + release.Version + " opened with the Quantix 28-setting profile. Confirm the import result in the Inspector window.") : ("NVIDIA Profile Inspector " + release.Version + " downloaded and verified successfully."), release.Version);
		}
		catch (Exception ex)
		{
			_logging.Error("GpuTools", "NVIDIA Profile Inspector setup failed.", ex);
			return new GpuToolResult(Success: false, "Could not prepare NVIDIA Profile Inspector: " + ex.Message);
		}
		finally
		{
			TryDeleteFile(temporaryArchive);
			if (stagingDirectory != null && Directory.Exists(stagingDirectory))
			{
				try
				{
					Directory.Delete(stagingDirectory, recursive: true);
				}
				catch
				{
				}
			}
		}
	}

	private static HttpClient CreateHttpClient()
	{
		HttpClient httpClient = new HttpClient();
		httpClient.Timeout = TimeSpan.FromSeconds(90.0);
		httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Quantix-Elite/3.3");
		httpClient.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
		return httpClient;
	}

	private async Task<ReleaseAsset> ResolveReleaseAsync(HttpClient client)
	{
		_ = 3;
		try
		{
			using CancellationTokenSource releaseLookupTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(12.0));
			using HttpResponseMessage response = await client.GetAsync("https://api.github.com/repos/Orbmu2k/nvidiaProfileInspector/releases/latest", releaseLookupTimeout.Token);
			response.EnsureSuccessStatusCode();
			ReleaseAsset result;
			await using (Stream stream = await response.Content.ReadAsStreamAsync())
			{
				GitHubRelease gitHubRelease = await JsonSerializer.DeserializeAsync<GitHubRelease>(stream, new JsonSerializerOptions
				{
					PropertyNameCaseInsensitive = true
				});
				GitHubAsset gitHubAsset = gitHubRelease?.Assets?.SingleOrDefault((GitHubAsset item) => string.Equals(item.Name, "nvidiaProfileInspector.zip", StringComparison.OrdinalIgnoreCase));
				string text = gitHubAsset?.Digest;
				if (gitHubRelease == null || gitHubRelease.Prerelease || gitHubRelease.Draft || gitHubAsset == null || string.IsNullOrWhiteSpace(gitHubRelease.TagName) || string.IsNullOrWhiteSpace(gitHubAsset.BrowserDownloadUrl) || string.IsNullOrWhiteSpace(text) || !text.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
				{
					throw new InvalidDataException("GitHub did not return a verifiable stable release.");
				}
				result = new ReleaseAsset(gitHubRelease.TagName, gitHubAsset.BrowserDownloadUrl, text.Substring("sha256:".Length));
			}
			return result;
		}
		catch (Exception ex)
		{
			_logging.Warn("GpuTools", "Latest release lookup failed; using verified stable fallback " + FallbackRelease.Version + ": " + ex.Message);
			return FallbackRelease;
		}
	}

	private static void ValidateRelease(ReleaseAsset release)
	{
		if (!Uri.TryCreate(release.DownloadUrl, UriKind.Absolute, out Uri result) || result.Scheme != Uri.UriSchemeHttps || !string.Equals(result.Host, "github.com", StringComparison.OrdinalIgnoreCase) || !result.AbsolutePath.StartsWith("/Orbmu2k/nvidiaProfileInspector/releases/download/", StringComparison.OrdinalIgnoreCase) || release.Sha256.Length != 64 || release.Sha256.Any((char character) => !Uri.IsHexDigit(character)))
		{
			throw new InvalidDataException("The release metadata did not pass source validation.");
		}
	}

	private static async Task DownloadArchiveAsync(HttpClient client, string url, string destination)
	{
		using HttpResponseMessage response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
		response.EnsureSuccessStatusCode();
		if (response.Content.Headers.ContentLength > 26214400)
		{
			throw new InvalidDataException("The download is larger than the allowed NVIDIA tool archive size.");
		}
		await using Stream source = await response.Content.ReadAsStreamAsync();
		await using FileStream target = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
		byte[] buffer = new byte[81920];
		long total = 0L;
		int num;
		while ((num = await source.ReadAsync(buffer)) > 0)
		{
			total += num;
			if (total > 26214400)
			{
				throw new InvalidDataException("The download exceeded the allowed NVIDIA tool archive size.");
			}
			await target.WriteAsync(buffer.AsMemory(0, num));
		}
	}

	private static async Task<string> CalculateSha256Async(string path)
	{
		string result;
		await using (FileStream stream = File.OpenRead(path))
		{
			result = Convert.ToHexString(await SHA256.HashDataAsync(stream)).ToLowerInvariant();
		}
		return result;
	}

	private static void ExtractVerifiedArchive(string archivePath, string destinationRoot)
	{
		string value = Path.GetFullPath(destinationRoot) + Path.DirectorySeparatorChar;
		long num = 0L;
		using ZipArchive zipArchive = ZipFile.OpenRead(archivePath);
		foreach (ZipArchiveEntry entry in zipArchive.Entries)
		{
			num += entry.Length;
			if (num > 104857600)
			{
				throw new InvalidDataException("The archive expands beyond the allowed size.");
			}
			string fullPath = Path.GetFullPath(Path.Combine(destinationRoot, entry.FullName));
			if (!fullPath.StartsWith(value, StringComparison.OrdinalIgnoreCase))
			{
				throw new InvalidDataException("The archive contains an unsafe path.");
			}
			if (string.IsNullOrEmpty(entry.Name))
			{
				Directory.CreateDirectory(fullPath);
				continue;
			}
			Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
			entry.ExtractToFile(fullPath, overwrite: true);
		}
	}

	private static async Task WriteEmbeddedProfileAsync(string destination)
	{
		await using Stream source = Assembly.GetExecutingAssembly().GetManifestResourceStream("QuantixTweakingUtility.QuantixNvidiaProfile") ?? throw new InvalidOperationException("The Quantix NVIDIA profile is missing.");
		await using FileStream target = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None);
		await source.CopyToAsync(target);
	}

	private static void EnsureChildPath(string root, string child)
	{
		string value = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
		if (!Path.GetFullPath(child).StartsWith(value, StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidOperationException("The NVIDIA tool path is outside the Quantix tools directory.");
		}
	}

	private static void DeleteDirectoryWithinRoot(string root, string directory)
	{
		EnsureChildPath(root, directory);
		Directory.Delete(directory, recursive: true);
	}

	private static void TryDeleteFile(string? path)
	{
		if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
		{
			return;
		}
		try
		{
			File.Delete(path);
		}
		catch
		{
		}
	}
}
