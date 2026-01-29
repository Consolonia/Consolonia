using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;

namespace Consolonia.Themes.Templates.Controls.Helpers
{
    public sealed class PopupPlacementToOuterMargin : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return (PlacementMode)value switch
            {
                PlacementMode.Top or PlacementMode.TopEdgeAlignedLeft or PlacementMode.TopEdgeAlignedRight =>
                    new Thickness(0, 0, 0, -1),
                PlacementMode.Bottom or PlacementMode.BottomEdgeAlignedLeft or PlacementMode.BottomEdgeAlignedRight =>
                    new Thickness(0, -1, 0, 0),
                PlacementMode.Left or PlacementMode.LeftEdgeAlignedBottom or PlacementMode.LeftEdgeAlignedTop =>
                    new Thickness(0, 0, -1, 0),
                PlacementMode.Right or PlacementMode.RightEdgeAlignedBottom or PlacementMode.RightEdgeAlignedTop =>
                    new Thickness(-1, 0, 0, 0),
                _ => new Thickness(0)
            };
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}