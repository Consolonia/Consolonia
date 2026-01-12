using System;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Styling;
using Avalonia.Threading;
using Consolonia.Themes.Infrastructure;

namespace Consolonia.Themes;

/// <summary>
/// Auto-includes DataGrid styles/resources for the current Consolonia theme family.
/// </summary>
public class AutoDataGridStyles : AutoThemeStylesBase
{
    private static readonly Uri DataGridUri = new(
        "avares://Consolonia.Controls.DataGrid/Themes/Templates/Controls/DataGrid.axaml");

    private static readonly Uri ModernRowUri = new(
        "avares://Consolonia.Controls.DataGrid/Themes/Modern/Controls/DataGridRow.axaml");

    protected override void ComposeForFamily(string family)
    {
        switch (family)
        {
            case ModernThemeKey:
                IncludeStyle(ModernRowUri);
                break;
            case TurboVisionThemeKey:
                IncludeStyle(DataGridUri);
                break;
        }
    }
}
