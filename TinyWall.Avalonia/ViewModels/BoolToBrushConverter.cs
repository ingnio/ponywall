using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace pylorak.TinyWall.ViewModels
{
    /// <summary>
    /// IValueConverter instances for turning a bool into a brush. Used by
    /// HistoryWindow's drill-down pane for the ALLOW/BLOCK badge.
    /// </summary>
    public sealed class BoolToBrushConverter : IValueConverter
    {
        private readonly IBrush _trueBrush;
        private readonly IBrush _falseBrush;

        private BoolToBrushConverter(IBrush trueBrush, IBrush falseBrush)
        {
            _trueBrush = trueBrush;
            _falseBrush = falseBrush;
        }

        /// <summary>True → red (Block), False → green (Allow).</summary>
        public static readonly BoolToBrushConverter BlockAllow = new(
            new SolidColorBrush(Color.Parse("#C43E3E")),
            new SolidColorBrush(Color.Parse("#3E9B4F")));

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is bool b)
                return b ? _trueBrush : _falseBrush;
            return _falseBrush;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }
}
