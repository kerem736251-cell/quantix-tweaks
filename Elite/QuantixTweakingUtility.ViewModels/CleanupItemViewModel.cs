using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel.__Internals;
using QuantixTweakingUtility.Models;

namespace QuantixTweakingUtility.ViewModels;

public sealed class CleanupItemViewModel : ObservableObject
{
	[ObservableProperty]
	private bool _isSelected;

	[ObservableProperty]
	private long _reclaimableBytes;

	[ObservableProperty]
	private string _status;

	public CleanupTarget Target { get; }

	public string ReclaimableDisplay
	{
		get
		{
			if (ReclaimableBytes != 0L)
			{
				return FormatBytes(ReclaimableBytes);
			}
			return "—";
		}
	}

	public bool IsCleanable => Target.IsCleanable;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.2.0.0")]
	[ExcludeFromCodeCoverage]
	public bool IsSelected
	{
		get
		{
			return _isSelected;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_isSelected, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.IsSelected);
				_isSelected = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.IsSelected);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.2.0.0")]
	[ExcludeFromCodeCoverage]
	public long ReclaimableBytes
	{
		get
		{
			return _reclaimableBytes;
		}
		set
		{
			if (!EqualityComparer<long>.Default.Equals(_reclaimableBytes, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.ReclaimableBytes);
				_reclaimableBytes = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.ReclaimableBytes);
				OnPropertyChanged("ReclaimableDisplay");
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.2.0.0")]
	[ExcludeFromCodeCoverage]
	public string Status
	{
		get
		{
			return _status;
		}
		[MemberNotNull("_status")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_status, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.Status);
				_status = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.Status);
			}
		}
	}

	public CleanupItemViewModel(CleanupTarget target)
	{
		Target = target;
		Status = (target.IsCleanable ? "Not scanned" : target.UnavailableReason);
	}

	public void Update(CleanupScanResult result)
	{
		ReclaimableBytes = result.ReclaimableBytes;
		Status = result.Status;
		IsSelected = Target.IsCleanable && result.ReclaimableBytes > 0;
		OnPropertyChanged("ReclaimableDisplay");
	}

	private static string FormatBytes(long bytes)
	{
		string[] array = new string[4] { "B", "KB", "MB", "GB" };
		double num = bytes;
		int num2 = 0;
		while (num >= 1024.0 && num2 < array.Length - 1)
		{
			num /= 1024.0;
			num2++;
		}
		return $"{num:0.#} {array[num2]}";
	}
}
