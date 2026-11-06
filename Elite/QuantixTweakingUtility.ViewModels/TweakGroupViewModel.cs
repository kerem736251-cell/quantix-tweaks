using System.Collections.ObjectModel;

namespace QuantixTweakingUtility.ViewModels;

public sealed class TweakGroupViewModel
{
	public required string Name { get; init; }

	public ObservableCollection<TweakRowViewModel> Rows { get; } = new ObservableCollection<TweakRowViewModel>();
}
