using System.Globalization;
using System.Windows.Data;

namespace Nova.App.ViewModels;

public sealed class BoolToGlyphConverter : IValueConverter
{
    public string TrueGlyph { get; set; } = string.Empty;
    public string FalseGlyph { get; set; } = string.Empty;

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? TrueGlyph : FalseGlyph;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
