using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using ElwMeteo.Desktop.Platform;
using ElwMeteo.Presentation.Platform;

namespace ElwMeteo.Desktop;

/// <summary>
/// The handful of conversions the views need.
///
/// Avalonia binds visibility to a boolean directly (IsVisible), so the two
/// visibility converters the Windows head needs collapse into one here: a
/// non-empty string means "show this".
/// </summary>
public static class Converters
{
    /// <summary>True when the string has content — bound straight to IsVisible.</summary>
    public static readonly IValueConverter StringNotEmpty =
        new FuncValueConverter<string?, bool>(s => !string.IsNullOrWhiteSpace(s));

    public static readonly IValueConverter Not =
        new FuncValueConverter<bool, bool>(b => !b);

    /// <summary>A view model names a colour; only the view knows what a brush is.</summary>
    public static readonly IValueConverter ColourToBrush =
        new FuncValueConverter<UiColour, IBrush>(c => c.ToBrush());

    public static readonly IValueConverter CountToBool =
        new FuncValueConverter<int, bool>(n => n > 0);
}
