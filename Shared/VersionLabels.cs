using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace QuantixTweakingUtility.Services;

public static class VersionLabels
{
    public static string Version => typeof(VersionLabels).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(VersionLabels).Assembly.GetName().Version!.ToString(3);

    public static void Refresh(DependencyObject root)
    {
        if (root is TextBlock label && label.Text.StartsWith("PRO / v"))
            label.Text = "PRO / v" + Version;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            Refresh(VisualTreeHelper.GetChild(root, i));
    }
}
