using System.Threading.Tasks;
using QuantixTweakingUtility.Models;

namespace QuantixTweakingUtility.Services;

public interface INetworkInfoService
{
	Task<NetworkSnapshot> GetSnapshotAsync();

	Task<(bool Success, string Message)> TestLatencyAsync();

	Task<(bool Success, string Message)> FlushDnsAsync();
	Task<(bool Success, string Message)> FlushDnsAndRenewAsync();
}

