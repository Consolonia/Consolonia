using Avalonia.Controls;
using Avalonia.Controls.Metadata;

namespace Consolonia.Controls
{
    /// <summary>
    ///     BorderPanel is a control that combines a Panel and a Border.
    /// </summary>
    /// <remarks>
    ///     * It removes a lot of copy-pasta border templates making it easier for a theme to control the border appearance for
    ///     popups.
    ///     * It is smart about LineBrushes with Edge style, applying background to non-edge borders and no background to edge
    ///     borders.
    ///     * It automatically crops the margin of the panel when hosted in a popup to avoid gaps between the button and the
    ///     popup.
    /// </remarks>
    [TemplatePart(PART_Border, typeof(Border))]
    public class BorderPanel : ContentControl
    {
        // ReSharper disable InconsistentNaming
#pragma warning disable CA1707 // Identifiers should not contain underscores
        public const string PART_Border = "PART_Border";
#pragma warning restore CA1707 // Identifiers should not contain underscores
        // ReSharper restore InconsistentNaming
    }
}