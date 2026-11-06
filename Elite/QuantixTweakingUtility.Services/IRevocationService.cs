using System.Threading.Tasks;

namespace QuantixTweakingUtility.Services;

public interface IRevocationService
{
	Task<RevocationCheckResult> CheckAsync(string licenseId);
}
