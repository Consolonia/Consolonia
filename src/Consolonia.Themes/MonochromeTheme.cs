using System;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;

namespace Consolonia.Themes
{
    public class MonochromeTheme : Styles
    {
        public MonochromeTheme(IServiceProvider serviceProvider = null)
        {
            AvaloniaXamlLoader.Load(serviceProvider, this);
        }
    }
}
