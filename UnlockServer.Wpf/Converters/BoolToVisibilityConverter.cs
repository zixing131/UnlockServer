using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace UnlockServer.Converters
{
    /// <summary>
    /// 布尔值到可见性转换器
    /// </summary>
    public class BoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool bValue = false;
            if (value is bool b)
            {
                bValue = b;
            }

            // 如果参数为 "Inverse"，则反转
            if (parameter != null && parameter.ToString() == "Inverse")
            {
                bValue = !bValue;
            }

            return bValue ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is Visibility visibility)
            {
                bool result = visibility == Visibility.Visible;
                if (parameter != null && parameter.ToString() == "Inverse")
                {
                    result = !result;
                }
                return result;
            }
            return false;
        }
    }
}

