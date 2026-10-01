using Consolonia.Themes.Infrastructure;

namespace Consolonia.Controls.DataGrid
{
    /// <summary>
    ///     Auto-includes DataGrid styles/resources for the current Consolonia theme family.
    /// </summary>
    public class AutoDataGridStyles : AutoThemeStylesBase
    {
        protected override void ComposeForFamily(string family)
        {
            switch (family)
            {
                case ModernThemeKey:
                    IncludeStyle(new ModernDataGridStyles());
                    break;
                case TurboVisionThemeKey:
                    IncludeStyle(new TurboVisionDataGridStyles());
                    break;
            }
        }
    }
}