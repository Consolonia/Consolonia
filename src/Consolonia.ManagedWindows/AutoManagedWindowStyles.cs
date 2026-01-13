using System;
using Consolonia.Themes.Infrastructure;

namespace Consolonia.Themes
{
    /// <summary>
    /// Auto-includes Managed Windows styles based on the ConsoloniaThemeFamily resource.
    /// Supports TurboVision and Modern themes.
    /// </summary>
    public class AutoManagedWindowStyles : AutoThemeStylesBase
    {
        protected override void ComposeForFamily(string family)
        {
            switch (family)
            {
                case TurboVisionThemeKey:
                    IncludeStyle(new Uri("avares://Consolonia.ManagedWindows/Themes/TurboVision/TurboVision.axaml"));
                    break;
                case ModernThemeKey:
                    IncludeStyle(new Uri("avares://Consolonia.ManagedWindows/Themes/Modern/Modern.axaml"));
                    break;
            }
        }
    }
}
