using System;
using System.IO;
using System.Reflection;
using System.Windows;
using Microsoft.Win32;
using QuantixLicensing;

namespace QuantixTweakingUtility.Services;

public static class OfflineUpdateUi
{
	public static void Import(Window owner)
	{
		OpenFileDialog openFileDialog = new OpenFileDialog
		{
			Filter = "Signed license update (*.qrv)|*.qrv"
		};
		if (openFileDialog.ShowDialog(owner) != true)
		{
			return;
		}
		try
		{
			ImportFile(openFileDialog.FileName);
			MessageBox.Show(owner, "License update imported. Restart the app to apply the updated license status.", "Quantix license update", MessageBoxButton.OK, MessageBoxImage.Asterisk);
			Application.Current.Shutdown();
		}
		catch (Exception ex)
		{
			MessageBox.Show(owner, ex.Message, "License update rejected", MessageBoxButton.OK, MessageBoxImage.Hand);
		}
	}

	public static void ImportFile(string path)
	{
		using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("QuantixTweakingUtility.LicensePublicKey");
		using StreamReader streamReader = new StreamReader(stream);
		OfflineRevocations.Import(File.ReadAllText(path), streamReader.ReadToEnd(), "quantix-elite");
	}
}
