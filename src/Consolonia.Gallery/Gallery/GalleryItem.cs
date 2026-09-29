using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Consolonia.Gallery.Gallery.GalleryViews;

// ReSharper disable MemberCanBePrivate.Global

namespace Consolonia.Gallery.Gallery
{
    internal class GalleryItem(string name, Type type, Func<UserControl> create)
    {
        private static readonly GalleryItem[] Items =
        [
            Register<GalleryAnimations>(),
            Register<GalleryAnsiArt>(),
            Register<GalleryAutoCompleteBox>(),
            Register<GalleryBorders>(),
            Register<GalleryButton>(),
            Register<GalleryButtonSpinner>(),
            Register<GalleryCalendar>(),
            Register<GalleryCalendarPicker>(),
            Register<GalleryCanvas>(),
            Register<GalleryCarousel>(),
            Register<GalleryCheckBox>(),
            Register<GalleryColors>(),
            Register<GalleryComboBox>(),
            Register<GalleryDataGrid>(),
            Register<GalleryDialog>(),
            Register<GalleryDragAndDrop>(),
            Register<GalleryEvents>(),
            Register<GalleryExpander>(),
            Register<GalleryFlyout>(),
            Register<GalleryFonts>(),
            Register<GalleryGradientBrush>(),
            Register<GalleryGridSplitter>(),
            Register<GalleryIcons>(),
            Register<GalleryImage>(),
            Register<GalleryLabel>(),
            Register<GalleryListBox>(),
            Register<GalleryMenu>(),
            Register<GalleryMessageBox>(),
            Register<GalleryNotifications>(),
            Register<GalleryNumericUpDown>(),
            Register<GalleryPlatform>(),
            Register<GalleryProgressBar>(),
            Register<GalleryRadioButton>(),
            Register<GalleryRelativePanel>(),
            Register<GalleryScrollViewer>(),
            Register<GallerySlider>(),
            Register<GallerySpring>(),
            Register<GalleryStorage>(),
            Register<GalleryTabControl>(),
            Register<GalleryTextBlock>(),
            Register<GalleryTextBox>(),
            Register<GalleryToggleSwitch>(),
            Register<GalleryTooltip>(),
            Register<GalleryTransitioningContent>(),
            Register<GalleryTreeView>(),
            Register<GalleryWelcome>(),
            Register<GalleryWindows>()
        ];

        private readonly Func<UserControl> _create = create;

        public Type Type { get; } = type;

        public string Name { get; } = name;

        public UserControl Create() => _create();

        public static IEnumerable<GalleryItem> Enumerated => Items.OrderBy(item => GalleryOrderAttribute.GetOrder(item.Type));

        private static GalleryItem Register<T>() where T : UserControl, new()
        {
            const string galleryPrefix = "Gallery";
            return new GalleryItem(typeof(T).Name[galleryPrefix.Length..], typeof(T), () => new T());
        }
    }

    public class GalleryItemConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null) return null;

            return ((GalleryItem)value).Create();
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}