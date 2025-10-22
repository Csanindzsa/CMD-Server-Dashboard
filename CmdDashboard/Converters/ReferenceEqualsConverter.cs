using System;
using System.Globalization;
using System.Windows.Data;

namespace CmdDashboard.Converters;

public class ReferenceEqualsConverter : IMultiValueConverter
{
    public object? Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values is not { Length: >= 2 })
        {
            return false;
        }

        return Equals(values[0], values[1]);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
