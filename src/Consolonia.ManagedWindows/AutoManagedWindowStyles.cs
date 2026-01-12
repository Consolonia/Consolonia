using System;
using Avalonia.Controls;
using Consolonia.Themes.Infrastructure;
using Iciclecreek.Avalonia.WindowManager;

namespace Consolonia.Themes;

/// <summary>
/// Auto-includes Managed Windows styles based on the ConsoloniaThemeFamily resource.
/// - Modern → Base.axaml
/// - TurboVision* → TurboVision.axaml
/// </summary>
public class AutoManagedWindowStyles : AutoThemeStylesBase
{
    private static readonly Uri ModernBaseUri = new("avares://Consolonia.ManagedWindows/Themes/Base.axaml");
    private static readonly Uri TurboVisionUri = new("avares://Consolonia.ManagedWindows/Themes/TurboVision/TurboVision.axaml");

    protected override void ComposeForFamily(string family)
    {
        switch (family)
        {
            case TurboVisionThemeKey:
                IncludeStyle(TurboVisionUri);
                break;
            case ModernThemeKey:
                IncludeStyle(ModernBaseUri);
                break;
        }
    }
}
