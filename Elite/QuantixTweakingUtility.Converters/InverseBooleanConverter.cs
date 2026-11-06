using System;
using System.Globalization;
using System.Windows.Data;

namespace QuantixTweakingUtility.Converters;

public sealed class InverseBooleanConverter : IValueConverter
{
	public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
	{
		if (!(value is bool flag))
		{
			return value;
		}
		return !flag;
	}

	public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
	{
		if (!(value is bool flag))
		{
			return value;
		}
		return !flag;
	}
}
