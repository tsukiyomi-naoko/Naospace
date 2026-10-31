using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using Nova.Core.Models;

namespace Nova.App;

public sealed class NullToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }
    public object Convert(object? value, Type t, object? p, CultureInfo c)
    {
        var visible = value is not null && !(value is string s && s.Length == 0);
        if (Invert) visible = !visible;
        return visible ? Visibility.Visible : Visibility.Collapsed;
    }
    public object ConvertBack(object? v, Type t, object? p, CultureInfo c) => throw new NotSupportedException();
}

public sealed class BoolToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }
    public object Convert(object? value, Type t, object? p, CultureInfo c) =>
        (value is true) ^ Invert ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object? v, Type t, object? p, CultureInfo c) => v is Visibility.Visible ^ Invert;
}

public sealed class HexToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type t, object? p, CultureInfo c)
    {
        try
        {
            if (value is string s && s.Length > 0 && ColorConverter.ConvertFromString(s) is Color col)
            {
                var b = new SolidColorBrush(col);
                b.Freeze();
                return b;
            }
        }
        catch (FormatException) { }
        return Brushes.SlateGray;
    }
    public object ConvertBack(object? v, Type t, object? p, CultureInfo c) => throw new NotSupportedException();
}

/// <summary>Segoe Fluent Icons glyph per pin kind.</summary>
public sealed class PinGlyphConverter : IValueConverter
{
    public object Convert(object? value, Type t, object? p, CultureInfo c) => value switch
    {
        PinKind.Folder => "",
        PinKind.File => "",
        PinKind.Application => "",
        PinKind.Script => "",
        PinKind.Url => "",
        PinKind.Command => "",
        _ => "",
    };
    public object ConvertBack(object? v, Type t, object? p, CultureInfo c) => throw new NotSupportedException();
}

public sealed class SortIndicatorConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type t, object? p, CultureInfo c)
    {
        if (values.Length < 2 || values[0] is not SortField active || p is not string field) return "";
        if (!string.Equals(active.ToString(), field, StringComparison.OrdinalIgnoreCase)) return "";
        return values.Length > 1 && values[1] is true ? " ▾" : " ▴";
    }
    public object[] ConvertBack(object v, Type[] t, object? p, CultureInfo c) => throw new NotSupportedException();
}
