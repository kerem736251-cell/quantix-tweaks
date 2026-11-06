using System.Threading.Tasks;

namespace QuantixTweakingUtility.Services;

public interface IGpuToolService
{
	Task<GpuToolResult> PrepareNvidiaProfileInspectorAsync(bool openInspector = true);
}
