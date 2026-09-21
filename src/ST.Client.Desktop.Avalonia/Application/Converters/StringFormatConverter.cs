using System.Collections.Generic;
using System.Globalization;
using System.Linq;
#if !__MOBILE__
using Avalonia.Data.Converters;
#else
using Xamarin.Forms;
#endif

namespace System.Application.Converters
{
    public class StringFormatConverter : IValueConverter, IMultiValueConverter
    {
        const string Size = "size";
        const string Money = "money";

        object? Format(object? value, object? parameter, CultureInfo? culture)
        {
            if (value is not string str) str = value?.ToString() ?? string.Empty;
            if (parameter is not string para) para = parameter?.ToString() ?? string.Empty;
            if (!string.IsNullOrEmpty(para))
            {
                if (string.Equals(para, Size, StringComparison.OrdinalIgnoreCase)
                    && double.TryParse(str, out double b))
                {
                    (var length, string unit) = IOPath.GetSize(b);
                    if (length == 0)
                        return "0 B";
                    return $"{length:###,###.##} {unit}";
                }
                else if (decimal.TryParse(str, out decimal d))
                {
                    // 已移除 ECurrencyCode（SteamKit2）与 CurrencyCode 两个币种分支：
                    // 二者分别依赖已裁掉的 SteamKit2 与 CurrencyCodeEnumExtensions，
                    // 且只被已删除的「捐赠排行」页面使用。
                    if (string.Equals(para, Money, StringComparison.OrdinalIgnoreCase))
                    {
                        return d.ToString("C", culture);
                    }
                }
                else
                {
                    return str.Format(para);
                }
            }
            return str;
        }


        public object? Convert(object? value, Type? targetType, object? parameter, CultureInfo? culture)
        {
            return Format(value, parameter, culture);
        }

        public object? Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            IList<object?> values_ = values;
            return Convert(values_, targetType, parameter, culture);
        }

        public object? Convert(IList<object?> values, Type? targetType, object? parameter, CultureInfo? culture)
        {
            var format = values.FirstOrDefault()?.ToString() ?? string.Empty;
            if (format == string.Empty || string.Equals("(Unset)", format.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }
            if (parameter is not null && values.Count >= 2)
            {
                if (string.Equals(parameter.ToString(), Money, StringComparison.OrdinalIgnoreCase))
                {
                    return Format(values[0], values[1] ?? Money, culture);
                }
                else
                {
                    return Format(values[0], parameter, culture);
                }
            }

            var stringValues = values.Select(x => (x is not string str) ? x?.ToString() ?? string.Empty : str);
            var args = stringValues.Skip(1).ToArray();
            return format.Format(args);
        }

        public object? ConvertBack(object? value, Type? targetType, object? parameter, CultureInfo? culture) => throw new NotImplementedException();

        public object[]? ConvertBack(object? value, Type[]? targetTypes, object? parameter, CultureInfo? culture) => throw new NotImplementedException();
    }
}
