using System;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading.Tasks;
using QuantixTweakingUtility.Models;

namespace QuantixTweakingUtility.Services;

public sealed class NetworkInfoService : INetworkInfoService
{
	private readonly ILoggingService _logging;

	public NetworkInfoService(ILoggingService logging)
	{
		_logging = logging;
	}

    public async Task<NetworkSnapshot> GetSnapshotAsync()
    {
        var result = await NetworkWorkflow.RunAsync("Inspect");
        var connection = result.Connection;
        if (!result.Success || connection == null)
            return new NetworkSnapshot { ConnectionState = result.Message, IsConnected = false };
        var nic = NetworkInterface.GetAllNetworkInterfaces().FirstOrDefault(n => n.Id.Trim('{','}').Equals(connection.Id, StringComparison.OrdinalIgnoreCase));
        var properties = nic?.GetIPProperties();
        return new NetworkSnapshot {
            AdapterName = connection.Adapter, ConnectionState = connection.Status, ConnectionType = connection.Type,
            LinkSpeed = connection.LinkSpeed, DnsServers = connection.Dns,
            IPv4Address = properties?.UnicastAddresses.FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork)?.Address.ToString() ?? "Unavailable",
            IPv6Address = properties?.UnicastAddresses.FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetworkV6 && !a.Address.IsIPv6LinkLocal)?.Address.ToString() ?? "Unavailable",
            IsConnected = connection.Index > 0
        };
    }

	public async Task<(bool Success, string Message)> TestLatencyAsync()
	{
		try
		{
			using Ping ping = new Ping();
			PingReply pingReply = await ping.SendPingAsync("1.1.1.1", 3000);
			if (pingReply.Status == IPStatus.Success)
			{
				return (Success: true, Message: $"Latency to 1.1.1.1: {pingReply.RoundtripTime} ms");
			}
			return (Success: false, Message: $"Latency test did not complete: {pingReply.Status}.");
		}
		catch (Exception ex)
		{
			_logging.Error("NetworkInfo", "Latency test failed.", ex);
			return (Success: false, Message: "Latency test could not reach the network.");
		}
	}

	public Task<(bool Success, string Message)> FlushDnsAsync() =>
		RunIpConfigAsync("/flushdns", "DNS resolver cache flushed.", "DNS flush");

	public async Task<(bool Success, string Message)> FlushDnsAndRenewAsync()
	{
		var inspection = await NetworkWorkflow.RunAsync("Inspect");
		if (!inspection.Success || inspection.Connection is not { Physical: true } connection || string.IsNullOrWhiteSpace(connection.Adapter))
			return (false, "A connected physical adapter is required for DHCP renewal.");
		if (connection.Adapter.IndexOfAny(new[] { '*', '?' }) >= 0)
			return (false, "Rename the adapter without wildcard characters before renewing its lease through Elite.");
		var flush = await FlushDnsAsync();
		if (!flush.Success)
			return flush;
		var renewal = await RunIpConfigAsync("/renew", "DHCP lease renewed on " + connection.Adapter + ".", "DHCP renewal", connection.Adapter);
		return (renewal.Success, flush.Message + " " + renewal.Message);
	}

	private async Task<(bool Success, string Message)> RunIpConfigAsync(string argument, string success, string action, string? adapter = null)
	{
		try
		{
			ProcessStartInfo startInfo = new ProcessStartInfo
			{
				FileName = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "ipconfig.exe"),
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				UseShellExecute = false,
				CreateNoWindow = true
			};
			startInfo.ArgumentList.Add(argument);
			if (adapter != null) startInfo.ArgumentList.Add(adapter);
			using Process process = Process.Start(startInfo);
			if (process == null)
			{
				return (Success: false, Message: "Windows could not start ipconfig.");
			}
			Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
			Task<string> errorTask = process.StandardError.ReadToEndAsync();
			using var timeout = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(45));
			try
			{
				await process.WaitForExitAsync(timeout.Token);
			}
			catch (OperationCanceledException)
			{
				if (!process.HasExited)
					process.Kill();
				await process.WaitForExitAsync();
				await Task.WhenAll(outputTask, errorTask);
				return (false, action + " timed out. Check the connection before retrying.");
			}
			string output = await outputTask;
			string text = await errorTask;
			if (process.ExitCode == 0)
			{
				_logging.Info("NetworkInfo", success);
				return (Success: true, Message: success);
			}
			_logging.Warn("NetworkInfo", action + " failed: " + text + output);
			return (Success: false, Message: string.IsNullOrWhiteSpace(text) ? action + " failed. See the log for details." : text.Trim());
		}
		catch (Exception ex)
		{
			_logging.Error("NetworkInfo", action + " failed.", ex);
			return (Success: false, Message: action + " failed. See the log for details.");
		}
	}

	private static string FormatLinkSpeed(long bitsPerSecond)
	{
		if (bitsPerSecond <= 0)
		{
			return "Unknown";
		}
		if (bitsPerSecond < 1000000000)
		{
			return $"{(double)bitsPerSecond / 1000000.0:0.#} Mbps";
		}
		return $"{(double)bitsPerSecond / 1000000000.0:0.#} Gbps";
	}
}


