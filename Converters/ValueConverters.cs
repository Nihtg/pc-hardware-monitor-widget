using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace MiniHardwareWidget.Converters;

public class TempToColorConverter : IValueConverter
{
    private static readonly SolidColorBrush CoolBrush = new(Color.FromRgb(78, 204, 163));     // #4ECCA3 (Mint green)
    private static readonly SolidColorBrush WarmBrush = new(Color.FromRgb(255, 211, 105));    // #FFD369 (Warm amber)
    private static readonly SolidColorBrush HotBrush = new(Color.FromRgb(255, 87, 87));       // #FF5757 (Bright red)
    private static readonly SolidColorBrush MutedBrush = new(Color.FromRgb(140, 145, 160));   // #8C91A0 (Gray)

    static TempToColorConverter()
    {
        CoolBrush.Freeze();
        WarmBrush.Freeze();
        HotBrush.Freeze();
        MutedBrush.Freeze();
    }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is float temp)
        {
            if (temp <= 0) return MutedBrush;
            if (temp < 65) return CoolBrush;
            if (temp < 80) return WarmBrush;
            return HotBrush;
        }
        return CoolBrush;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
}

public class SpeedFormatterConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is double bytesPerSec)
        {
            if (bytesPerSec < 1024) return $"{bytesPerSec:0} B/s";
            if (bytesPerSec < 1024 * 1024) return $"{bytesPerSec / 1024.0:0.0} KB/s";
            if (bytesPerSec < 1024 * 1024 * 1024) return $"{bytesPerSec / (1024.0 * 1024.0):0.1} MB/s";
            return $"{bytesPerSec / (1024.0 * 1024.0 * 1024.0):0.2} GB/s";
        }
        return "0 B/s";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
}

public class BooleanToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; } = false;

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool b = value is true;
        if (Invert) b = !b;
        return b ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
}
