using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Xml.Linq;

namespace QuantixTweakingUtility;

internal static class BrandMarks
{
    private static readonly Dictionary<string, Geometry> Marks = new(StringComparer.Ordinal);

    internal static FrameworkElement Create(string brand, double size)
    {
        if (brand == "quantix") return new Image { Source = App.EliteLogo(), Width = size, Height = size };
        if (brand is not ("discord" or "tiktok" or "x")) throw new ArgumentException("Unknown brand mark.", nameof(brand));
        if (!Marks.TryGetValue(brand, out var geometry))
        {
            var uri = new Uri($"pack://application:,,,/QuantixTweakingUtility;component/assets/brand-{brand}.svg");
            using var resource = Application.GetResourceStream(uri)?.Stream ?? throw new InvalidOperationException("Missing brand asset: " + brand);
            var svg = XDocument.Load(resource);
            string data = svg.Root!.Elements().Single(element => element.Name.LocalName == "path").Attribute("d")!.Value;
            geometry = Geometry.Parse("F1 " + data);
            geometry.Freeze();
            Marks.Add(brand, geometry);
        }
        return new System.Windows.Shapes.Path
        {
            Data = geometry, Width = size, Height = size, Stretch = Stretch.Uniform,
            Fill = brand == "discord" ? new SolidColorBrush(Color.FromRgb(88, 101, 242)) : Brushes.White
        };
    }
}
