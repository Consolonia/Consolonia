using System;
using Avalonia.Input;
using Avalonia.Platform;
using Consolonia.Core.Text;

namespace Consolonia.Core.Infrastructure
{
    public enum NotSupportedRequestCode
    {
        /// <summary>
        ///     this is case of OverlayPopupHost.Render
        ///     todo: check, seems not used anymore
        /// </summary>
        OverlayPopupHostRender = 9,

        /// <summary>
        ///     Attempt to create an embeddable window.
        ///     Handler must return <see cref="IWindowImpl" />
        /// </summary>
        CreateEmbeddableWindow = 13,

        /// <summary>
        ///     Pressed key is not supported.
        ///     Handler receives <see cref="ConsoleKey" /> and must return <see cref="Key" />
        /// </summary>
        InputNotSupported = 62722,

        /// <summary>
        ///     Needs to detect a color at position over a brush
        /// </summary>
        ColorFromBrushPosition = 751,

        /// <summary>
        ///     Attempt to draw a geometry that is not supported by the current platform.
        /// </summary>
        DrawGeometryNotSupported = 5,

        /// <summary>
        ///     Attempt to draw a rounded or non-uniform rectangle.
        /// </summary>
        DrawingRoundedOrNonUniformRectandle = 10,

        /// <summary>
        ///     Attempt to draw a box shadow that is not supported by the current platform.
        /// </summary>
        DrawingBoxShadowNotSupported = 11,

        /// <summary>
        ///     Must return <see cref="GlyphRunImpl" />
        /// </summary>
        DrawGlyphRunNotSupported = 17,
        PushClipWithRoundedRectNotSupported = 2,
        PushOpacityNotSupported = 7,
        TransformLineWithRotationNotSupported = 16,
        ExtractColorFromPenNotSupported = 6,
        DrawStringWithNonSolidColorBrush = 4,
        BackgroundWasNotColoredWhileMapping = 62144,
        ConsoleWindowCreateTrayIconNotSupported = 62723,
        ConsoleWindowCreateEmbeddableTopLevelNotSupported = 62724,
        ConsoleWindowHideNotSupported = 62725,
        ConsoleWindowActivateNotSupported = 62726,
        ConsoleWindowSetTopmostNotSupported = 62727,
        ConsoleWindowSetParentNotSupported = 62728,
        ConsoleWindowSetEnabledNotSupported = 62729,
        ConsoleWindowSetSystemDecorationsNotSupported = 62730,
        ConsoleWindowSetIconNotSupported = 62731,
        ConsoleWindowShowTaskbarIconNotSupported = 62732,
        ConsoleWindowCanResizeNotSupported = 62733,
        ConsoleWindowBeginMoveDragNotSupported = 62734,
        ConsoleWindowBeginResizeDragNotSupported = 62735,
        ConsoleWindowBeginResizeNotSupported = 62736,
        ConsoleWindowMoveNotSupported = 62737,
        ConsoleWindowSetMinMaxSizeNotSupported = 62738,
        ConsoleWindowSetExtendClientAreaToDecorationsHintNotSupported = 62739,
        ConsoleWindowSetExtendClientAreaChromeHintsNotSupported = 62740,
        ConsoleWindowSetExtendClientAreaTitleBarHeightHintNotSupported = 62741,
        ConsoleWindowSetCanMinimizeNotSupported = 62742,
        ConsoleWindowSetCanMaximizeNotSupported = 62743,
        DrawEllipseNotSupported = 62744,
        PushClipRegionNotSupported = 62745,
        PushLayerNotSupported = 62746,
        DrawRegionNotSupported = 62747,
        CubicBezierToNotSupported = 62748,
        QuadraticBezierToNotSupported = 62749,
        BitmapsNotSupported = 62750,
        VerticalTextDecorationNotSupported = 62751
    }
}