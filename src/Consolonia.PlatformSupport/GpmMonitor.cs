using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Threading;
using Consolonia.Core.Helpers;
using Consolonia.Core.InternalHelpers;

namespace Consolonia.PlatformSupport
{
    /// <summary>
    ///     implementation with GPM (General Purpose Mouse) support for TTY environments
    /// </summary>
    internal class GpmMonitor : IDisposable
    {
        private static readonly FlagTranslator<GpmModifiers, RawInputModifiers>
            GpmModifiersToRawInputModifiers = new([
                (GpmModifiers.Shift, RawInputModifiers.Shift),
                (GpmModifiers.Alt, RawInputModifiers.Alt),
                (GpmModifiers.Control, RawInputModifiers.Control)
            ]);

        private static readonly FlagTranslator<GpmButtons, RawInputModifiers>
            GpmButtonsToRawInputModifiers = new([
                (GpmButtons.Left, RawInputModifiers.LeftMouseButton),
                (GpmButtons.Middle, RawInputModifiers.MiddleMouseButton),
                (GpmButtons.Right, RawInputModifiers.RightMouseButton)
            ]);

        private static readonly FlagTranslator<GpmButtons, RawPointerEventType>
            GpmButtonsToRawPointerEventDownType = new([
                (GpmButtons.Left, RawPointerEventType.LeftButtonDown),
                (GpmButtons.Middle, RawPointerEventType.MiddleButtonDown),
                (GpmButtons.Right, RawPointerEventType.RightButtonDown)
            ]);

        private static readonly FlagTranslator<GpmButtons, RawPointerEventType>
            GpmButtonsToRawPointerEventUpType = new([
                (GpmButtons.Left, RawPointerEventType.LeftButtonUp),
                (GpmButtons.Middle, RawPointerEventType.MiddleButtonUp),
                (GpmButtons.Right, RawPointerEventType.RightButtonUp)
            ]);

        private readonly CancellationTokenSource _gpmCancellation;
        private GpmConnect _gpmConnection;
        private int _gpmFd = -1;
        private bool _gpmInitialized;

        private Task _pumpTask;

        public GpmMonitor()
        {
            _gpmCancellation = new CancellationTokenSource();
            // Set up GPM connection
            _gpmConnection = new GpmConnect
            {
                EventMask = 0xffff, // Receive all events
                DefaultMask = 0, // Explicitly disable all default handling
                MinMod = 0, // Accept events with no modifiers or more
                MaxMod = 0xffff // Accept events with any/all modifiers (0xFFFF or ~0)
            };

            _gpmFd = Gpm.Open(ref _gpmConnection, 0);
            if (_gpmFd < 0) 
                throw new InvalidOperationException("Failed to open GPM connection");

            _gpmInitialized = true;
            _pumpTask = PumpGpmEventsAsync(_gpmCancellation.Token);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        public event Action<RawPointerEventType, Point, Vector?, RawInputModifiers> MouseEvent;

        private async Task PumpGpmEventsAsync(CancellationToken cancellationToken)
        {
            await Helper.WaitDispatcherInitialized();

            while (!cancellationToken.IsCancellationRequested)
                if (Gpm.GetEvent(out GpmEvent gpmEvent) > 0)
                    ProcessGpmEvent(gpmEvent);
        }


        private void ProcessGpmEvent(GpmEvent gpmEvent)
        {
            // Convert 1-based GPM coordinates to 0-based
            var point = new Point(gpmEvent.X - 1, gpmEvent.Y - 1);

            // Get combined modifiers (tracked keyboard + GPM)
            RawInputModifiers modifiers = GpmModifiersToRawInputModifiers.Translate(gpmEvent.Modifiers) |
                                          GpmButtonsToRawInputModifiers.Translate(gpmEvent.Buttons);

            // Handle wheel events - GPM can report wheel in multiple ways
            // Wheel events have dx=0, dy=0 (no movement) and specific type patterns
            if (gpmEvent.WheelDeltaX != 0 || gpmEvent.WheelDeltaY != 0)
                RaiseMouseEvent(RawPointerEventType.Wheel, point,
                    new Vector(gpmEvent.WheelDeltaX, gpmEvent.WheelDeltaY), modifiers);
            else if (gpmEvent.Type.HasFlag(GpmEventType.Move) || gpmEvent.Type.HasFlag(GpmEventType.Drag))
                RaiseMouseEvent(RawPointerEventType.Move, point, null, modifiers);
            else if (gpmEvent.Type.HasFlag(GpmEventType.Down))
                RaiseMouseEvent(GpmButtonsToRawPointerEventDownType.Translate(gpmEvent.Buttons), point, null,
                    modifiers);
            else if (gpmEvent.Type.HasFlag(GpmEventType.Up))
                RaiseMouseEvent(GpmButtonsToRawPointerEventUpType.Translate(gpmEvent.Buttons), point, null, modifiers);
        }


        protected void RaiseMouseEvent(RawPointerEventType eventType, Point point, Vector? wheelDelta,
            RawInputModifiers modifiers)
        {
            // System.Diagnostics.Debug.WriteLine($"Mouse event: {eventType} [{point}] {wheelDelta} {modifiers}");
            if (MouseEvent != null)
                Dispatcher.UIThread.Invoke(() => { MouseEvent?.Invoke(eventType, point, wheelDelta, modifiers); },
                    DispatcherPriority.Input);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing && _gpmInitialized)
            {
                _gpmCancellation.Cancel();
                try
                {
                    _pumpTask?.Wait();
                }
                catch (TaskCanceledException)
                {
                    /* ignored */
                }

                if (_gpmFd >= 0)
                {
                    _ = Gpm.Close();
                    _gpmFd = -1;
                }

                _gpmCancellation?.Dispose();
            }
        }
    }
}