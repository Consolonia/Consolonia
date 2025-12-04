using System;
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
using Consolonia.Core.InternalHelpers;
using Unix.Terminal;

namespace Consolonia.PlatformSupport
{
    /// <summary>
    /// Console implementation with GPM (General Purpose Mouse) support for TTY environments
    /// This wraps CursesConsole and adds libgpm mouse input handling
    /// </summary>
    public class GpmConsole : CursesConsole
    {
        private readonly CancellationTokenSource _gpmCancellation;
        private int _gpmFd = -1;
        private GpmNativeBindings.Gpm_Connect _gpmConnection;
        private bool _gpmInitialized;
        private GpmNativeBindings.GpmButtons _lastButtonState = 0;
        private Point _lastPosition = new Point(0, 0);
        private RawInputModifiers _currentModifiers = RawInputModifiers.None;

        public GpmConsole()
        {
            _gpmCancellation = new CancellationTokenSource();
            InitializeGpm();
        }

        public override void PrepareConsole()
        {
            base.PrepareConsole();

            //disable cursers mouse, we are talking to GPM directly.
            Curses.mousemask(0,
                out Curses.Event _);

        }

        private void InitializeGpm()
        {
            try
            {
                // Set up GPM connection
                _gpmConnection = new GpmNativeBindings.Gpm_Connect
                {
                    eventMask = (ushort)(
                        GpmNativeBindings.GpmEventType.GPM_MOVE |
                        GpmNativeBindings.GpmEventType.GPM_DOWN |
                        GpmNativeBindings.GpmEventType.GPM_UP |
                        GpmNativeBindings.GpmEventType.GPM_DRAG |
                        GpmNativeBindings.GpmEventType.GPM_SINGLE |
                        GpmNativeBindings.GpmEventType.GPM_DOUBLE |
                        GpmNativeBindings.GpmEventType.GPM_TRIPLE |
                        GpmNativeBindings.GpmEventType.GPM_HARD
                    ),
                    defaultMask = 0,
                    minMod = 0,
                    maxMod = 0
                };

                _gpmFd = GpmNativeBindings.Gpm_Open(ref _gpmConnection, 0);
                if (_gpmFd < 0)
                {
                    return;
                }

                // Hide the GPM hardware cursor (we draw our own in software)
                try
                {
                    GpmNativeBindings.Gpm_DrawPointer(-1, -1, 0);
                }
                catch (EntryPointNotFoundException)
                {
                    // Function not available, cursor will remain visible
                    System.Diagnostics.Debug.WriteLine("Gpm_DrawPointer not available, GPM cursor will be visible");
                }

                _gpmInitialized = true;
                Task.Run(GpmEventLoop, _gpmCancellation.Token);
            }
            catch (DllNotFoundException)
            {
                return;
            }
            catch (EntryPointNotFoundException)
            {
                return;
            }
        }

        private async Task GpmEventLoop()
        {
            await Helper.WaitDispatcherInitialized();

            while (!_gpmCancellation.Token.IsCancellationRequested && !Disposed)
            {
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
                    int result = WaitForGpmEvent(10); // 10ms timeout

                    if (result > 0)
                    {
                        // GPM event available
                        int eventResult = GpmNativeBindings.Gpm_GetEvent(out GpmNativeBindings.Gpm_Event gpmEvent);

                        if (eventResult > 0)
                        {
                            await DispatchInputAsync(() => ProcessGpmEvent(gpmEvent));
                        }
                    }
                    else if (result == 0)
                    {
                        // Timeout - continue loop
                        continue;
                    }
                    else
                    {
                        // Error - wait a bit before retrying
                        await Task.Delay(100, _gpmCancellation.Token);
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
                        tv_sec = timeoutMs / 1000,
                        tv_usec = (timeoutMs % 1000) * 1000
                    };

                    // Call select
                    return Select(_gpmFd + 1, readfds, IntPtr.Zero, IntPtr.Zero, ref timeout);
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

        private void ProcessGpmEvent(GpmNativeBindings.Gpm_Event gpmEvent)
        {
            // Debug.WriteLine($"GPM Event: buttons={gpmEvent.buttons} (0x{gpmEvent.buttons:X2}), type={gpmEvent.type}, x={gpmEvent.x}, y={gpmEvent.y}, dx={gpmEvent.dx}, dy={gpmEvent.dy}, clicks={gpmEvent.clicks}");

            // Convert 1-based GPM coordinates to 0-based
            var point = new Point(gpmEvent.x - 1, gpmEvent.y - 1);

            // Translate GPM modifiers to Avalonia modifiers
            var modifiers = RawInputModifiers.None;
            if ((gpmEvent.modifiers & 0x01) != 0) modifiers |= RawInputModifiers.Shift;
            if ((gpmEvent.modifiers & 0x04) != 0) modifiers |= RawInputModifiers.Control;
            if ((gpmEvent.modifiers & 0x08) != 0) modifiers |= RawInputModifiers.Alt;

            var buttons = (GpmNativeBindings.GpmButtons)gpmEvent.buttons;

            // Add button state to modifiers for regular mouse events
            if (buttons.HasFlag(GpmNativeBindings.GpmButtons.GPM_B_LEFT))
                modifiers |= RawInputModifiers.LeftMouseButton;
            if (buttons.HasFlag(GpmNativeBindings.GpmButtons.GPM_B_MIDDLE))
                modifiers |= RawInputModifiers.MiddleMouseButton;
            if (buttons.HasFlag(GpmNativeBindings.GpmButtons.GPM_B_RIGHT))
                modifiers |= RawInputModifiers.RightMouseButton;

            _currentModifiers = modifiers;
            _lastPosition = point;

            // Handle wheel events - GPM can report wheel in multiple ways
            // Wheel events have dx=0, dy=0 (no movement) and specific type patterns
            if (gpmEvent.dx == 0 && gpmEvent.dy == 0)
            {
                // Pattern 1: buttons=0x18 (B_FOURTH+B_UP), type=MFLAG = scroll UP
                if (gpmEvent.type.HasFlag(GpmNativeBindings.GpmEventType.GPM_MFLAG) &&
                    buttons.HasFlag(GpmNativeBindings.GpmButtons.GPM_B_UP))
                {
                    // Debug.WriteLine("GPM: Wheel UP detected (MFLAG pattern)");
                    RaiseMouseEvent(RawPointerEventType.Wheel, point, new Vector(0, 1), modifiers);
                    _lastButtonState = buttons;
                    return;
                }

                // Pattern 2: buttons=0, type=MOVE = scroll DOWN
                if (gpmEvent.type == GpmNativeBindings.GpmEventType.GPM_MOVE &&
                    buttons == GpmNativeBindings.GpmButtons.GPM_B_NONE)
                {
                    // Debug.WriteLine("GPM: Wheel DOWN detected (MOVE+no buttons pattern)");
                    RaiseMouseEvent(RawPointerEventType.Wheel, point, new Vector(0, -1), modifiers);
                    _lastButtonState = buttons;
                    return;
                }
            }

            if (gpmEvent.type.HasFlag(GpmNativeBindings.GpmEventType.GPM_MOVE) ||
                     gpmEvent.type.HasFlag(GpmNativeBindings.GpmEventType.GPM_DRAG))
            {
                RaiseMouseEvent(RawPointerEventType.Move, point, null, modifiers);
            }
            else if (gpmEvent.type.HasFlag(GpmNativeBindings.GpmEventType.GPM_DOWN))
            {
                //Debug.WriteLine($"GPM: Button DOWN {buttons} detected");
                ProcessButtonDown(buttons, point, modifiers);
            }
            else if (gpmEvent.type.HasFlag(GpmNativeBindings.GpmEventType.GPM_UP))
            {
                //Debug.WriteLine($"GPM: Button UP {buttons} detected");
                ProcessButtonUp(buttons, point, modifiers);
            }

            _lastButtonState = buttons;
        }


        private void ProcessButtonDown(GpmNativeBindings.GpmButtons buttons, Point point, RawInputModifiers modifiers)
        {
            if (buttons.HasFlag(GpmNativeBindings.GpmButtons.GPM_B_LEFT))
            {
                RaiseMouseEvent(RawPointerEventType.LeftButtonDown, point, null, modifiers);
            }

            if (buttons.HasFlag(GpmNativeBindings.GpmButtons.GPM_B_MIDDLE))
            {
                RaiseMouseEvent(RawPointerEventType.MiddleButtonDown, point, null, modifiers);
            }

            if (buttons.HasFlag(GpmNativeBindings.GpmButtons.GPM_B_RIGHT))
            {
                RaiseMouseEvent(RawPointerEventType.RightButtonDown, point, null, modifiers);
            }
        }

        private void ProcessButtonUp(GpmNativeBindings.GpmButtons buttons, Point point, RawInputModifiers modifiers)
        {
            // Check for transitions from pressed to released
            if (_lastButtonState.HasFlag(GpmNativeBindings.GpmButtons.GPM_B_LEFT))
            {
                RaiseMouseEvent(RawPointerEventType.LeftButtonUp, point, null, modifiers);
            }

            if (_lastButtonState.HasFlag(GpmNativeBindings.GpmButtons.GPM_B_MIDDLE))
            {
                RaiseMouseEvent(RawPointerEventType.MiddleButtonUp, point, null, modifiers);
            }

            if (_lastButtonState.HasFlag(GpmNativeBindings.GpmButtons.GPM_B_RIGHT))
            {
                RaiseMouseEvent(RawPointerEventType.RightButtonUp, point, null, modifiers);
            }
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
                        GpmNativeBindings.Gpm_Close();
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

        #region P/Invoke for select()

        [StructLayout(LayoutKind.Sequential)]
        private struct Timeval
        {
            public long tv_sec;   // Changed from int to long for 64-bit compatibility
            public long tv_usec;  // Changed from int to long for 64-bit compatibility
        }

        [DllImport("libc", EntryPoint = "select", SetLastError = true)]
        private static extern int Select(int nfds, IntPtr readfds, IntPtr writefds, IntPtr exceptfds, ref Timeval timeout);

        #endregion
    }
}
