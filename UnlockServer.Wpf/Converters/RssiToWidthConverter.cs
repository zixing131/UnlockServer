using System;
using System.Globalization;
using System.Windows.Data;

namespace UnlockServer.Converters
{
    /// <summary>
    /// RSSI 值到宽度转换器（用于信号强度条）
    /// </summary>
    public class RssiToWidthConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            double maxWidth = 100;
            if (parameter != null && double.TryParse(parameter.ToString(), out double mw))
            {
                maxWidth = mw;
            }

            if (value is short rssi)
            {
                // RSSI 范围通常是 -100 到 -30
                // 转换为 0-100% 的宽度
                double percentage = Math.Max(0, Math.Min(100, (rssi + 100) * 1.43)); // -100 -> 0%, -30 -> 100%
                return maxWidth * percentage / 100;
            }
            
            return 0;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}

