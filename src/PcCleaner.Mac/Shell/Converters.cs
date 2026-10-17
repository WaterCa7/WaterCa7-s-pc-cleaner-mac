using System.Globalization;
using Avalonia;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace PcCleaner.Shell;

/// <summary>Icon name ("Home") → the geometry "Icon.Home" from Themes/Icons.axaml.</summary>
public sealed class IconConverter : IValueConverter
{
    public static readonly IconConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is string name ? ThemeResources.Find("Icon." + name) as Geometry : null;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => BindingOperations.DoNothing;
}

/// <summary>Theme brush key ("DangerBrush") → the brush, so view models can pick colors without referencing Avalonia.</summary>
public sealed class BrushKeyConverter : IValueConverter
{
    public static readonly BrushKeyConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is string key ? ThemeResources.Find(key) as IBrush : null;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => BindingOperations.DoNothing;
}

internal static class ThemeResources
{
    public static object? Find(string key)
    {
        var app = Application.Current;
        return app is not null && app.TryGetResource(key, app.ActualThemeVariant, out var value) ? value : null;
    }
}
