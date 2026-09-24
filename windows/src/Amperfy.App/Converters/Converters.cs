using Amperfy.Core.Common;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace Amperfy.App.Converters;

public sealed class BoolToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var b = value is bool v && v;
        if (Invert) b = !b;
        return b ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        value is Visibility v && (v == Visibility.Visible) != Invert;
}

public sealed class NullToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var visible = value is not null && (value is not string s || s.Length > 0);
        if (Invert) visible = !visible;
        return visible ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotSupportedException();
}

/// Seconds (int/double) -> "3:05"
public sealed class DurationConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) => value switch
    {
        int i => i.AsColonDurationString(),
        double d => d.AsColonDurationString(),
        long l => ((int)l).AsColonDurationString(),
        _ => "",
    };

    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotSupportedException();
}
