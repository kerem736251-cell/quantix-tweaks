using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace QuantixTweakingUtility.ViewModels;

public sealed class TweakTabViewModel : ObservableObject
{
	private string searchText = "";

	private string refreshStatus = "";

	public string Key { get; set; }

	public string Title { get; set; }

	public string Description { get; set; }

	public ObservableCollection<TweakGroupViewModel> Groups { get; } = new ObservableCollection<TweakGroupViewModel>();

	public string SearchText
	{
		get
		{
			return searchText;
		}
		set
		{
			if (SetProperty(ref searchText, value, "SearchText"))
			{
				OnPropertyChanged("FilteredGroups");
				OnPropertyChanged("FilterSummary");
			}
		}
	}

	public string RefreshStatus
	{
		get
		{
			return refreshStatus;
		}
		private set
		{
			SetProperty(ref refreshStatus, value, "RefreshStatus");
		}
	}

	public int TweakCount => Groups.Sum((TweakGroupViewModel g) => g.Rows.Count);

	public IEnumerable<TweakGroupViewModel> FilteredGroups => from g in Groups.Select(delegate(TweakGroupViewModel g)
		{
			TweakGroupViewModel tweakGroupViewModel = new TweakGroupViewModel
			{
				Name = g.Name
			};
			foreach (TweakRowViewModel item in g.Rows.Where(Matches))
			{
				tweakGroupViewModel.Rows.Add(item);
			}
			return tweakGroupViewModel;
		})
		where g.Rows.Count > 0
		select g;

	public string FilterSummary => $"{Groups.Sum((TweakGroupViewModel g) => g.Rows.Count(Matches))} of {TweakCount} controls shown";

	public IAsyncRelayCommand RefreshCommand { get; }

	private bool Matches(TweakRowViewModel row)
	{
		if (!string.IsNullOrWhiteSpace(SearchText))
		{
			return (row.Definition.Name + " " + row.Definition.Description + " " + row.Definition.Id + " " + row.Definition.RiskLevel).Contains(SearchText.Trim(), StringComparison.OrdinalIgnoreCase);
		}
		return true;
	}

	public TweakTabViewModel()
	{
		Groups.CollectionChanged += delegate(object? _, NotifyCollectionChangedEventArgs e)
		{
			if (e.NewItems != null)
			{
				foreach (TweakGroupViewModel newItem in e.NewItems)
				{
					newItem.Rows.CollectionChanged += delegate
					{
						Changed();
					};
				}
			}
			Changed();
		};
		RefreshCommand = new AsyncRelayCommand((Func<Task>)async delegate
		{
			RefreshStatus = "Checking current settings…";
			try
			{
				foreach (TweakRowViewModel item in Groups.SelectMany((TweakGroupViewModel g) => g.Rows))
				{
					if (!item.IsBusy)
					{
						await item.RefreshStateAsync();
					}
				}
				RefreshStatus = "Status refreshed. Busy controls are left to finish.";
			}
			catch (Exception ex)
			{
				RefreshStatus = "Refresh could not complete: " + ex.Message;
			}
		});
	}

	private void Changed()
	{
		OnPropertyChanged("FilteredGroups");
		OnPropertyChanged("FilterSummary");
		OnPropertyChanged("TweakCount");
	}
}
