using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel.__Internals;
using QuantixTweakingUtility.Models;

namespace QuantixTweakingUtility.ViewModels;

public sealed class StartupRowViewModel : ObservableObject
{
	[ObservableProperty]
	private bool _isBusy;

	public StartupItem Info { get; }

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.2.0.0")]
	[ExcludeFromCodeCoverage]
	public bool IsBusy
	{
		get
		{
			return _isBusy;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_isBusy, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.IsBusy);
				_isBusy = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.IsBusy);
			}
		}
	}

	public StartupRowViewModel(StartupItem info)
	{
		Info = info;
	}
}
