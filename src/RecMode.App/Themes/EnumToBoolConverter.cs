using System.Globalization;
using System.Windows.Data;

namespace RecMode.App.Themes;

/// <summary>
/// Two-way converter binding a <see cref="RadioButton.IsChecked"/> to an enum property: true when the bound
/// enum equals the <c>ConverterParameter</c> (given as the enum member name). Used for the segmented theme
/// selector and the accent colour swatches. ConvertBack returns the parameter enum only when checked, so the
/// unchecked RadioButton in a group doesn't clobber the selection.
/// </summary>
public sealed class EnumToBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is not null && parameter is not null &&
           string.Equals(value.ToString(), parameter.ToString(), StringComparison.Ordinal);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is true && parameter is not null)
        {
            Type enumType = Nullable.GetUnderlyingType(targetType) ?? targetType;
            return Enum.Parse(enumType, parameter.ToString()!);
        }

        return Binding.DoNothing;
    }
}
