using System;
using Avalonia;
using Avalonia.Input;
using Avalonia.Input.Raw;

namespace Consolonia.Core.Infrastructure
{
    /// <summary>
    ///     Target for routed input events — identifies which window should receive the event.
    /// </summary>
    public readonly struct InputTarget
    {
        public Action<RawInputEventArgs> InputCallback { get; init; }
        public IInputRoot InputRoot { get; init; }
        public Point LocalPoint { get; init; }
    }

    /// <summary>
    ///     Routes console input events to the correct window.
    ///     ConsoleSurface delegates routing decisions to this interface.
    ///     When no router is set, all events go to the main window.
    /// </summary>
    public interface IInputRouter
    {
        /// <summary>
        ///     Determines which window should receive a mouse event.
        ///     Handles hit-testing, pointer capture, and window activation internally.
        /// </summary>
        /// <returns>Target window info, or null to route to the main window.</returns>
        InputTarget? RouteMouseEvent(RawPointerEventType type, Point screenPoint);

        /// <summary>
        ///     Determines which window should receive keyboard events.
        /// </summary>
        /// <returns>Target window info, or null to route to the main window.</returns>
        InputTarget? RouteKeyboardEvent();
    }
}
