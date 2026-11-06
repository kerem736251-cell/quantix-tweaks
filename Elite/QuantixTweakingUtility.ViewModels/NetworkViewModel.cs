using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel.__Internals;
using CommunityToolkit.Mvvm.Input;
using QuantixTweakingUtility.Models;
using QuantixTweakingUtility.Services;

namespace QuantixTweakingUtility.ViewModels;

public sealed class NetworkViewModel : ObservableObject
{
	private readonly INetworkInfoService _networkInfo;
	public string ConnectionType { get; private set; } = "Detecting…";

	[ObservableProperty]
	private string _adapterName = "Detecting...";

	[ObservableProperty]
	private string _connectionState = "Detecting...";

	[ObservableProperty]
	private string _ipv4Address = "Detecting...";

	[ObservableProperty]
	private string _ipv6Address = "Detecting...";

	[ObservableProperty]
	private string _dnsServers = "Detecting...";

	[ObservableProperty]
	private string _linkSpeed = "Detecting...";

	[ObservableProperty]
	private bool _isConnected;

	[ObservableProperty]
	private bool _isBusy;

	[ObservableProperty]
	private string _actionStatus = "Live adapter details are read from Windows.";

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.2.0.0")]
	private AsyncRelayCommand? refreshCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.2.0.0")]
	private AsyncRelayCommand? testLatencyCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.2.0.0")]
	private AsyncRelayCommand? flushDnsCommand;

	public ObservableCollection<TweakRowViewModel> Tweaks { get; }

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.2.0.0")]
	[ExcludeFromCodeCoverage]
	public string AdapterName
	{
		get
		{
			return _adapterName;
		}
		[MemberNotNull("_adapterName")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_adapterName, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.AdapterName);
				_adapterName = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.AdapterName);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.2.0.0")]
	[ExcludeFromCodeCoverage]
	public string ConnectionState
	{
		get
		{
			return _connectionState;
		}
		[MemberNotNull("_connectionState")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_connectionState, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.ConnectionState);
				_connectionState = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.ConnectionState);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.2.0.0")]
	[ExcludeFromCodeCoverage]
	public string Ipv4Address
	{
		get
		{
			return _ipv4Address;
		}
		[MemberNotNull("_ipv4Address")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_ipv4Address, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.Ipv4Address);
				_ipv4Address = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.Ipv4Address);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.2.0.0")]
	[ExcludeFromCodeCoverage]
	public string Ipv6Address
	{
		get
		{
			return _ipv6Address;
		}
		[MemberNotNull("_ipv6Address")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_ipv6Address, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.Ipv6Address);
				_ipv6Address = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.Ipv6Address);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.2.0.0")]
	[ExcludeFromCodeCoverage]
	public string DnsServers
	{
		get
		{
			return _dnsServers;
		}
		[MemberNotNull("_dnsServers")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_dnsServers, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.DnsServers);
				_dnsServers = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.DnsServers);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.2.0.0")]
	[ExcludeFromCodeCoverage]
	public string LinkSpeed
	{
		get
		{
			return _linkSpeed;
		}
		[MemberNotNull("_linkSpeed")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_linkSpeed, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.LinkSpeed);
				_linkSpeed = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.LinkSpeed);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.2.0.0")]
	[ExcludeFromCodeCoverage]
	public bool IsConnected
	{
		get
		{
			return _isConnected;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_isConnected, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.IsConnected);
				_isConnected = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.IsConnected);
			}
		}
	}

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

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.2.0.0")]
	[ExcludeFromCodeCoverage]
	public string ActionStatus
	{
		get
		{
			return _actionStatus;
		}
		[MemberNotNull("_actionStatus")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_actionStatus, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.ActionStatus);
				_actionStatus = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.ActionStatus);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.2.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand RefreshCommand => refreshCommand ?? (refreshCommand = new AsyncRelayCommand(RefreshAsync));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.2.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand TestLatencyCommand => testLatencyCommand ?? (testLatencyCommand = new AsyncRelayCommand(TestLatencyAsync));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.2.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand FlushDnsCommand => flushDnsCommand ?? (flushDnsCommand = new AsyncRelayCommand(FlushDnsAsync));

	public NetworkViewModel(INetworkInfoService networkInfo, ITweakService tweaks)
	{
		_networkInfo = networkInfo;
		Tweaks = new ObservableCollection<TweakRowViewModel>(from tweak in tweaks.GetTweaksByCategory(TweakCategory.Network)
			select new TweakRowViewModel(tweak, tweaks));
		RefreshAsync();
	}

	[RelayCommand]
	private async Task RefreshAsync()
	{
		try
		{
			NetworkSnapshot networkSnapshot = await _networkInfo.GetSnapshotAsync();
			ConnectionType = networkSnapshot.ConnectionType;
			OnPropertyChanged(nameof(ConnectionType));
			AdapterName = networkSnapshot.AdapterName;
			ConnectionState = networkSnapshot.ConnectionState;
			Ipv4Address = networkSnapshot.IPv4Address;
			Ipv6Address = networkSnapshot.IPv6Address;
			DnsServers = networkSnapshot.DnsServers;
			LinkSpeed = networkSnapshot.LinkSpeed;
			IsConnected = networkSnapshot.IsConnected;
		}
		catch (Exception ex)
		{
			IsConnected = false;
			ConnectionState = "Status unavailable";
			NetworkViewModel networkViewModel = this;
			NetworkViewModel networkViewModel2 = this;
			NetworkViewModel networkViewModel3 = this;
			NetworkViewModel networkViewModel4 = this;
			string text = (LinkSpeed = "â€”");
			string text3 = (networkViewModel4.DnsServers = text);
			string text5 = (networkViewModel3.Ipv6Address = text3);
			string adapterName = (networkViewModel2.Ipv4Address = text5);
			networkViewModel.AdapterName = adapterName;
			ActionStatus = "Network refresh failed: " + ex.Message;
		}
	}

	[RelayCommand]
	private async Task TestLatencyAsync()
	{
		if (IsBusy)
		{
			return;
		}
		IsBusy = true;
		try
		{
			ActionStatus = (await _networkInfo.TestLatencyAsync()).Item2;
		}
		catch (Exception ex)
		{
			ActionStatus = "Network action failed: " + ex.Message;
		}
		finally
		{
			IsBusy = false;
		}
	}

	[RelayCommand]
	private async Task FlushDnsAsync()
	{
		if (IsBusy)
		{
			return;
		}
		IsBusy = true;
		try
		{
			ActionStatus = (await _networkInfo.FlushDnsAsync()).Item2;
		}
		catch (Exception ex)
		{
			ActionStatus = "Network action failed: " + ex.Message;
		}
		finally
		{
			IsBusy = false;
		}
	}
}
