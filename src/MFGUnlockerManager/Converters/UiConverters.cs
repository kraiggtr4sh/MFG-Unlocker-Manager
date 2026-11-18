using System.Globalization;
using System.Windows;
using System.Windows.Data;
using MFGUnlockerManager.Models;

namespace MFGUnlockerManager.Converters;

public sealed class NavIndexToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is int selected && parameter != null && int.TryParse(parameter.ToString(), out int target))
        {
            return selected == target ? Visibility.Visible : Visibility.Collapsed;
        }
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;
}

public sealed class StringNotEmptyToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var str = value as string;
        return !string.IsNullOrWhiteSpace(str) ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;
}

public sealed class InstallModeBooleanConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is InstallMode current && parameter is string targetStr &&
            Enum.TryParse<InstallMode>(targetStr, out var targetMode))
        {
            return current == targetMode;
        }
        return false;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is true && parameter is string targetStr &&
            Enum.TryParse<InstallMode>(targetStr, out var targetMode))
        {
            return targetMode;
        }
        return Binding.DoNothing;
    }
}
