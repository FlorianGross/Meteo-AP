using System.Globalization;
using System.Windows.Media;
using ElwMeteo.App.Platform;
using ElwMeteo.Presentation.Platform;
using System.Windows;
using System.Windows.Data;
using ElwMeteo.Core.Assessment;
using ElwMeteo.Core.Configuration;
using ElwMeteo.Core.Models;

namespace ElwMeteo.App;

/// <summary>Maps a hint severity to its accent colour.</summary>
public sealed class SeverityBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush Warning = new(Color.FromRgb(0xE6, 0x39, 0x46));
    private static readonly SolidColorBrush Caution = new(Color.FromRgb(0xF5, 0x9E, 0x0B));
    private static readonly SolidColorBrush Info = new(Color.FromRgb(0x3B, 0x82, 0xF6));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        HintSeverity.Warning => Warning,
        HintSeverity.Caution => Caution,
        _ => Info
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Maps a hint severity to a compact glyph.</summary>
public sealed class SeverityGlyphConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        HintSeverity.Warning => "!",
        HintSeverity.Caution => "△",
        _ => "i"
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Turns a DWD warning's colour string into a brush.</summary>
public sealed class WarningLevelBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not WarningLevel level)
        {
            return Brushes.Gray;
        }

        return level switch
        {
            WarningLevel.Minor => new SolidColorBrush(Color.FromRgb(0xFF, 0xD4, 0x00)),
            WarningLevel.Moderate => new SolidColorBrush(Color.FromRgb(0xFF, 0x8C, 0x00)),
            WarningLevel.Severe => new SolidColorBrush(Color.FromRgb(0xE1, 0x00, 0x2F)),
            WarningLevel.Extreme => new SolidColorBrush(Color.FromRgb(0x8B, 0x00, 0x00)),
            _ => new SolidColorBrush(Color.FromRgb(0x4A, 0x55, 0x68))
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Collapses an element when the bound boolean is false.</summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    /// <summary>Set to true to collapse when the value is *true* instead.</summary>
    public bool Invert { get; set; }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool flag = value is true;
        if (Invert)
        {
            flag = !flag;
        }

        return flag ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Negates a boolean — used to disable buttons while a fetch is running.</summary>
public sealed class NotConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is not true;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is not true;
}

/// <summary>Collapses an element when the bound string is empty.</summary>
public sealed class StringToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        string.IsNullOrWhiteSpace(value as string) ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>German labels for the position-source modes shown in the settings combo.</summary>
public sealed class LocationModeConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        LocationMode.Automatic => "Automatisch (GPS → IP → Standort)",
        LocationMode.GpsOnly => "Nur GPS-Empfänger",
        LocationMode.Manual => "Manuelle Koordinaten",
        _ => value?.ToString() ?? string.Empty
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// Turns a view model's plain <see cref="UiColour"/> into a WPF brush.
///
/// The view models name a colour but never build a brush — that is the one
/// decision that differs between the WPF and the Avalonia head, and keeping it
/// out of them is what lets both read the same view model.
/// </summary>
public sealed class UiColourToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is UiColour colour ? colour.ToBrush() : Brushes.Gray;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
