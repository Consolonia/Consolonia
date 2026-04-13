using System;
using Consolonia.Themes.Infrastructure;

namespace Consolonia.Modal
{
    public class ModalTheme : AutoThemeStylesBase
    {
        protected override void ComposeForFamily(string family)
        {
            IncludeStyle(new Uri("avares://Consolonia.Modal/Themes/DialogHost.axaml"));

            switch (family)
            {
                case TurboVisionThemeKey:
                case ModernThemeKey:
                    IncludeStyle(new Uri("avares://Consolonia.Modal/Themes/DialogWindow.axaml"));
                    break;
            }
        }
    }
}
