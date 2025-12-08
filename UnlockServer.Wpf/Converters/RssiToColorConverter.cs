using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace UnlockServer.Converters
{
    /// <summary>
    /// RSSI 值到颜色转换器
    /// </summary>
    public class RssiToColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is short rssi)
            {
                // 根据信号强度返回不同颜色
                if (rssi >= -50)
                    return new SolidColorBrush(Color.FromRgb(16, 185, 129));  // 优秀 - 绿色
                if (rssi >= -60)
                    return new SolidColorBrush(Color.FromRgb(132, 204, 22)); // 良好 - 黄绿色
                if (rssi >= -70)
                    return new SolidColorBrush(Color.FromRgb(245, 158, 11)); // 一般 - 橙色
                if (rssi >= -80)
                    return new SolidColorBrush(Color.FromRgb(239, 68, 68));  // 较弱 - 红色
                
                return new SolidColorBrush(Color.FromRgb(107, 114, 128));    // 极弱 - 灰色
            }
            
            return new SolidColorBrush(Color.FromRgb(107, 114, 128));
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}

