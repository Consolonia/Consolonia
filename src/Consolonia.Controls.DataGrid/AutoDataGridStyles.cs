using System;
using Avalonia.Controls;

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
        if (family == null)
        {
            Resources = new ResourceDictionary();
            return;
        }
        
        MergeResource(DataGridUri);

        
        if (!family.Equals("TurboVision", StringComparison.OrdinalIgnoreCase))
        {
            MergeResource(ModernRowUri);
        }
    }
}
