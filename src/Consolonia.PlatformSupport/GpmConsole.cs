using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Threading;
using Consolonia.Core.Helpers;
using Consolonia.Core.Infrastructure;
using Consolonia.Core.InternalHelpers;

namespace Consolonia.PlatformSupport
{
    /// <summary>
    ///     Console implementation with GPM (General Purpose Mouse) support for TTY environments
    ///     This wraps CursesConsole and adds libgpm mouse input handling
    /// </summary>
    public class GpmConsole : CursesConsole
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
                (GpmButtons.Right, RawInputModifiers.RightMouseButton),
                (GpmButtons.Fourth, RawInputModifiers.XButton1MouseButton)
            ]);

        private static readonly FlagTranslator<GpmButtons, RawPointerEventType>
            GpmButtonsToRawPointerEventDownType = new([
                (GpmButtons.Left, RawPointerEventType.LeftButtonDown),
                (GpmButtons.Middle, RawPointerEventType.MiddleButtonDown),
                (GpmButtons.Right, RawPointerEventType.RightButtonDown),
                (GpmButtons.Fourth, RawPointerEventType.XButton1Down)
            ]);

        private static readonly FlagTranslator<GpmButtons, RawPointerEventType>
            GpmButtonsToRawPointerEventUpType = new([
                (GpmButtons.Left, RawPointerEventType.LeftButtonUp),
                (GpmButtons.Middle, RawPointerEventType.MiddleButtonUp),
                (GpmButtons.Right, RawPointerEventType.RightButtonUp),
                (GpmButtons.Fourth, RawPointerEventType.XButton1Up)
            ]);

        private readonly CancellationTokenSource _gpmCancellation;
        private readonly CancellationToken _gpmToken;
        private GpmConnect _gpmConnection;
        private int _gpmFd = -1;
        private bool _gpmInitialized;
        private Socket _socket;

        public GpmConsole()
            : base(false)
        {
            _gpmCancellation = new CancellationTokenSource();
            _gpmToken = _gpmCancellation.Token;
            InitializeGpm();
        }


        private void InitializeGpm()
        {
            try
            {
                // Set up GPM connection
                _gpmConnection = new GpmConnect
                {
                    EventMask = 0xffff, // Receive all events
                    DefaultMask = 0, // Explicitly disable all default handling
                    MinMod = 0, // Accept events with no modifiers or more
                    MaxMod = 0xffff // Accept events with any/all modifiers (0xFFFF or ~0)
                };

                _gpmFd = Gpm.Open(ref _gpmConnection, 0);
                if (_gpmFd < 0) return;

                // Wrap the file descriptor in a Socket using the SafeSocketHandle constructor
                _socket = new Socket(new SafeSocketHandle(_gpmFd, false));

                // Hide the GPM hardware cursor (we draw our own in software)
                try
                {
                    _ = Gpm.DrawPointer(-1, -1, 0);
                }
                catch (EntryPointNotFoundException)
                {
                    // Function not available, cursor will remain visible
                    Debug.WriteLine("Gpm_DrawPointer not available, GPM cursor will be visible");
                }

                _gpmInitialized = true;
                Task.Run(GpmEventLoop, _gpmToken);
            }
            catch (DllNotFoundException)
            {
            }
            catch (EntryPointNotFoundException)
            {
            }
        }

        private async Task GpmEventLoop()
        {
            await Helper.WaitDispatcherInitialized();

            while (!_gpmToken.IsCancellationRequested && !Disposed)
                try
                {
                    // Check for pause
                    Task pauseTask = PauseTask;
                    if (pauseTask != null)
                    {
                        await pauseTask;
                        continue;
                    }

                    // Use select to wait for GPM events with timeout
                    int result = WaitForGpmEvent(1000); // 1000ms timeout

                    if (result > 0)
                    {
                        // Process ALL available events in one batch
                        // This reduces the overhead of DispatchInputAsync calls
                        var events = new List<GpmEvent>();

                        // Read first event (we know it's available from select)
                        int eventResult = Gpm.GetEvent(out GpmEvent gpmEvent);
                        if (eventResult > 0)
                        {
                            events.Add(gpmEvent);

                            // Console.WriteLine(gpmEvent.Dump());
                            // Keep reading while events are available (non-blocking)
                            // Use select with 0 timeout to check if more events are ready
                            while (WaitForGpmEvent(0) > 0)
                            {
                                eventResult = Gpm.GetEvent(out gpmEvent);
                                if (eventResult > 0)
                                    events.Add(gpmEvent);
                                // Console.WriteLine(gpmEvent.Dump());
                                else
                                    break;

                                // Safety limit to prevent infinite loop
                                if (events.Count >= 100)
                                    break;
                            }

                            // Process all events in one dispatcher call
                            if (events.Count > 0)
                                await DispatchInputAsync(() =>
                                {
                                    foreach (GpmEvent ev in events)
                                        ProcessGpmEvent(ev);
                                });
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Dispatcher.UIThread.Post(
                        () => throw new ConsoloniaException("Exception in GPM event processing loop", ex),
                        DispatcherPriority.MaxValue);
                    break;
                }
        }

        private int WaitForGpmEvent(int timeoutMs)
        {
            if (_gpmFd < 0) return -1;

            try
            {
                // Convert timeout to microseconds for Socket.Select
                int timeoutMicroseconds = timeoutMs * 1000;

                // Call Socket.Select
                // Create a list with a single socket for the GPM file descriptor
                var checkRead = new List<Socket>
                {
                    _socket
                };

                Socket.Select(checkRead, null, null, timeoutMicroseconds);

                // If the socket is still in the list, data is available
                return checkRead.Count > 0 ? 1 : 0;
            }
            catch (SocketException)
            {
                return -1;
            }
            catch (ObjectDisposedException)
            {
                return -1;
            }
        }

        private void ProcessGpmEvent(GpmEvent gpmEvent)
        {
            // Convert 1-based GPM coordinates to 0-based
            var point = new Point(gpmEvent.X - 1, gpmEvent.Y - 1);

            // Get combined modifiers (tracked keyboard + GPM)
            RawInputModifiers modifiers = GpmModifiersToRawInputModifiers.Translate(gpmEvent.Modifiers)
                                          | GpmButtonsToRawInputModifiers.Translate(gpmEvent.Buttons);

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

        protected override void Dispose(bool disposing)
        {
            if (disposing && _gpmInitialized)
            {
                _gpmCancellation?.Cancel();

                _socket?.Dispose();
                _socket = null;

                if (_gpmFd >= 0)
                {
                    _ = Gpm.Close();
                    _gpmFd = -1;
                }

                _gpmCancellation?.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}