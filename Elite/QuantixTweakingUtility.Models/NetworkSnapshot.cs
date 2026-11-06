namespace QuantixTweakingUtility.Models;

public sealed class NetworkSnapshot
{
	public string ConnectionType { get; init; } = "Unknown";

	public string AdapterName { get; init; } = "Unknown";

	public string ConnectionState { get; init; } = "Offline";

	public string IPv4Address { get; init; } = "Unknown";

	public string IPv6Address { get; init; } = "Unknown";

	public string DnsServers { get; init; } = "Unknown";

	public string LinkSpeed { get; init; } = "Unknown";

	public bool IsConnected { get; init; }
}
