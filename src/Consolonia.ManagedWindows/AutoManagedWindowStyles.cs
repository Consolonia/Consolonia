using Consolonia.Themes.Infrastructure;

namespace Consolonia.ManagedWindows
{
    /// <summary>
    ///     Auto-includes Managed Windows styles based on the ConsoloniaThemeFamily resource.
    ///     Supports TurboVision and Modern themes.
    /// </summary>
    public class AutoManagedWindowStyles : AutoThemeStylesBase
    {
        protected override void ComposeForFamily(string family)
        {
            switch (family)
            {
                case TurboVisionThemeKey:
                    IncludeStyle(new TurboVisionManagedWindowStyles());
                    break;
                case ModernThemeKey:
                    IncludeStyle(new ModernManagedWindowStyles());
                    break;
            }
        }
    }
}