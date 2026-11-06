using System.Threading.Tasks;
using QuantixTweakingUtility.Models;

namespace QuantixTweakingUtility.Services;

public interface ISystemInfoService
{
	Task<SystemSnapshot> GetStaticInfoAsync();
}
