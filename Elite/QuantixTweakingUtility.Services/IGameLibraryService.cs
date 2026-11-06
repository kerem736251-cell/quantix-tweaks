using System.Collections.Generic;
using System.Threading.Tasks;
using QuantixTweakingUtility.Models;

namespace QuantixTweakingUtility.Services;

public interface IGameLibraryService
{
	Task<List<DetectedGame>> ScanForInstalledGamesAsync();
}
