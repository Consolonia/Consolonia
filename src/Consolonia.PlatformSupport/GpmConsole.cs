using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Threading;
using Consolonia.Core.Helpers;
using Consolonia.Core.Infrastructure;

namespace Consolonia.PlatformSupport
{
    /// <summary>
    ///     Console implementation with GPM (General Purpose Mouse) support for TTY environments
    ///     This wraps CursesConsole and adds libgpm mouse input handling
    /// </summary>
    public class GpmConsole : CursesConsole
    {
        private readonly CancellationTokenSource _gpmCancellation;
        private GpmConnect _gpmConnection;
        private int _gpmFd = -1;
        private bool _gpmInitialized;

        public GpmConsole()
            : base(false)
        {
            _gpmCancellation = new CancellationTokenSource();
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
                Task.Run(GpmEventLoop, _gpmCancellation.Token);
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

            while (!_gpmCancellation.Token.IsCancellationRequested && !Disposed)
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
                }
        }

        private int WaitForGpmEvent(int timeoutMs)
        {
            if (_gpmFd < 0) return -1;

            try
            {
                // Use P/Invoke to call select() with the GPM file descriptor
                IntPtr readfds = Marshal.AllocHGlobal(128); // fd_set size
                try
                {
                    // Initialize fd_set
                    for (int i = 0; i < 128; i++)
                        Marshal.WriteByte(readfds, i, 0);

                    // Set the bit for our fd (fd_set is a bit array)
                    int byteIndex = _gpmFd / 8;
                    int bitIndex = _gpmFd % 8;
                    byte currentByte = Marshal.ReadByte(readfds, byteIndex);
                    currentByte |= (byte)(1 << bitIndex);
                    Marshal.WriteByte(readfds, byteIndex, currentByte);

                    // Set timeout
                    var timeout = new Timeval
                    {
                        Sec = timeoutMs / 1000,
                        Usec = timeoutMs % 1000 * 1000
                    };

                    // Call select
                    return Gpm.Select(_gpmFd + 1, readfds, IntPtr.Zero, IntPtr.Zero, ref timeout);
                }
                finally
                {
                    Marshal.FreeHGlobal(readfds);
                }
            }
            catch (DllNotFoundException)
            {
                return -1;
            }
            catch (EntryPointNotFoundException)
            {
                return -1;
            }
        }

        private void ProcessGpmEvent(GpmEvent gpmEvent)
        {
            // Convert 1-based GPM coordinates to 0-based
            var point = new Point(gpmEvent.X - 1, gpmEvent.Y - 1);

            // Get combined modifiers (tracked keyboard + GPM)
            var modifiers = RawInputModifiers.None;

            // Add GPM-reported modifiers (in case GPM does report them)
            if (gpmEvent.Modifiers.HasFlag(GpmModifiers.Shift))
                modifiers |= RawInputModifiers.Shift;
            if (gpmEvent.Modifiers.HasFlag(GpmModifiers.Control))
                modifiers |= RawInputModifiers.Control;
            if (gpmEvent.Modifiers.HasFlag(GpmModifiers.Alt))
                modifiers |= RawInputModifiers.Alt;

            // Add button state to modifiers for regular mouse events
            if (gpmEvent.Buttons.HasFlag(GpmButtons.Left))
                modifiers |= RawInputModifiers.LeftMouseButton;
            if (gpmEvent.Buttons.HasFlag(GpmButtons.Middle))
                modifiers |= RawInputModifiers.MiddleMouseButton;
            if (gpmEvent.Buttons.HasFlag(GpmButtons.Right))
                modifiers |= RawInputModifiers.RightMouseButton;


            // Handle wheel events - GPM can report wheel in multiple ways
            // Wheel events have dx=0, dy=0 (no movement) and specific type patterns
            if (gpmEvent.DeltaX == 0 && gpmEvent.DeltaY == 0)
            {
                // Pattern 1: buttons=0x18 (B_FOURTH+B_UP), type=MFLAG = scroll UP
                if (gpmEvent.Type.HasFlag(GpmEventType.MFlag) &&
                    gpmEvent.Buttons.HasFlag(GpmButtons.WheelUp))
                {
                    // Debug.WriteLine("GPM: Wheel UP detected (MFLAG pattern)");
                    RaiseMouseEvent(RawPointerEventType.Wheel, point, new Vector(0, 1), modifiers);
                    return;
                }

                // Pattern 2: buttons=0, type=MOVE = scroll DOWN
                if (gpmEvent.Type == GpmEventType.Move &&
                    gpmEvent.Buttons == GpmButtons.None)
                {
                    // Debug.WriteLine("GPM: Wheel DOWN detected (MOVE+no buttons pattern)");
                    RaiseMouseEvent(RawPointerEventType.Wheel, point, new Vector(0, -1), modifiers);
                    return;
                }
            }

            if (gpmEvent.Type.HasFlag(GpmEventType.Move) ||
                gpmEvent.Type.HasFlag(GpmEventType.Drag))
                RaiseMouseEvent(RawPointerEventType.Move, point, null, modifiers);
            else if (gpmEvent.Type.HasFlag(GpmEventType.Down))
                //Debug.WriteLine($"GPM: Button DOWN {buttons} detected");
                ProcessButtonDown(gpmEvent, point, modifiers);
            else if (gpmEvent.Type.HasFlag(GpmEventType.Up))
                //Debug.WriteLine($"GPM: Button UP {buttons} detected");
                ProcessButtonUp(gpmEvent, point, modifiers);
        }

        private void ProcessButtonDown(GpmEvent gpmEvent, Point point, RawInputModifiers modifiers)
        {
            if (gpmEvent.Buttons.HasFlag(GpmButtons.Left))
                RaiseMouseEvent(RawPointerEventType.LeftButtonDown, point, null, modifiers);

            if (gpmEvent.Buttons.HasFlag(GpmButtons.Middle))
                RaiseMouseEvent(RawPointerEventType.MiddleButtonDown, point, null, modifiers);

            if (gpmEvent.Buttons.HasFlag(GpmButtons.Right))
                RaiseMouseEvent(RawPointerEventType.RightButtonDown, point, null, modifiers);
        }

        private void ProcessButtonUp(GpmEvent gpmEvent, Point point, RawInputModifiers modifiers)
        {
            // Check for transitions from pressed to released
            if (gpmEvent.Buttons.HasFlag(GpmButtons.Left))
                RaiseMouseEvent(RawPointerEventType.LeftButtonUp, point, null, modifiers);

            if (gpmEvent.Buttons.HasFlag(GpmButtons.Middle))
                RaiseMouseEvent(RawPointerEventType.MiddleButtonUp, point, null, modifiers);

            if (gpmEvent.Buttons.HasFlag(GpmButtons.Right))
                RaiseMouseEvent(RawPointerEventType.RightButtonUp, point, null, modifiers);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _gpmInitialized)
            {
                _gpmCancellation?.Cancel();

                try
                {
                    if (_gpmFd >= 0)
                    {
                        _ = Gpm.Close();
                        _gpmFd = -1;
                    }
                }
                catch (DllNotFoundException)
                {
                    // Library unloaded, ignore
                }
                catch (EntryPointNotFoundException)
                {
                    // Entry point not found, ignore
                }

                _gpmCancellation?.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}