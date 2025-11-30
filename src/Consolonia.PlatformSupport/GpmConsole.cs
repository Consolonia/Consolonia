using System;
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

        private void InitializeGpm()
        {
            try
            {
                // Set up GPM connection to receive all mouse events
                _gpmConnection = new GpmNativeBindings.Gpm_Connect
                {
                    eventMask = (ushort)(
                        GpmNativeBindings.GpmEventType.GPM_MOVE |
                        GpmNativeBindings.GpmEventType.GPM_DOWN |
                        GpmNativeBindings.GpmEventType.GPM_UP |
                        GpmNativeBindings.GpmEventType.GPM_DRAG |
                        GpmNativeBindings.GpmEventType.GPM_SINGLE |
                        GpmNativeBindings.GpmEventType.GPM_DOUBLE |
                        GpmNativeBindings.GpmEventType.GPM_TRIPLE
                    ),
                    defaultMask = 0,
                    minMod = 0,
                    maxMod = 0
                };

                _gpmFd = GpmNativeBindings.Gpm_Open(ref _gpmConnection, 0);
                if (_gpmFd < 0)
                {
                    // GPM not available, fall back to ncurses mouse only
                    return;
                }

                _gpmInitialized = true;

                // Start GPM event loop
                Task.Run(GpmEventLoop, _gpmCancellation.Token);
            }
            catch (DllNotFoundException)
            {
                // libgpm not installed, fall back to ncurses mouse
            }
            catch (EntryPointNotFoundException ex)
            {
                // Wrong libgpm version
                System.Diagnostics.Debug.WriteLine($"GPM initialization failed - entry point not found: {ex.Message}");
            }
            catch (BadImageFormatException ex)
            {
                // Architecture mismatch
                System.Diagnostics.Debug.WriteLine($"GPM initialization failed - bad image format: {ex.Message}");
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
                    int result = WaitForGpmEvent(100); // 100ms timeout
                    
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
            // Convert 1-based GPM coordinates to 0-based
            var point = new Point(gpmEvent.x - 1, gpmEvent.y - 1);
            
            // Translate GPM modifiers to Avalonia modifiers
            var modifiers = RawInputModifiers.None;
            // Note: GPM modifiers are typically keyboard modifiers (Shift, Ctrl, Alt)
            // The exact values depend on the GPM implementation
            if ((gpmEvent.modifiers & 0x01) != 0) modifiers |= RawInputModifiers.Shift;
            if ((gpmEvent.modifiers & 0x04) != 0) modifiers |= RawInputModifiers.Control;
            if ((gpmEvent.modifiers & 0x08) != 0) modifiers |= RawInputModifiers.Alt;

            var buttons = (GpmNativeBindings.GpmButtons)gpmEvent.buttons;

            // Add button state to modifiers
            if (buttons.HasFlag(GpmNativeBindings.GpmButtons.GPM_B_LEFT))
                modifiers |= RawInputModifiers.LeftMouseButton;
            if (buttons.HasFlag(GpmNativeBindings.GpmButtons.GPM_B_MIDDLE))
                modifiers |= RawInputModifiers.MiddleMouseButton;
            if (buttons.HasFlag(GpmNativeBindings.GpmButtons.GPM_B_RIGHT))
                modifiers |= RawInputModifiers.RightMouseButton;

            _currentModifiers = modifiers;
            _lastPosition = point;

            // Process event type
            if (gpmEvent.type.HasFlag(GpmNativeBindings.GpmEventType.GPM_DOWN))
            {
                ProcessButtonDown(buttons, point, modifiers);
            }
            else if (gpmEvent.type.HasFlag(GpmNativeBindings.GpmEventType.GPM_UP))
            {
                ProcessButtonUp(buttons, point, modifiers);
            }
            else if (gpmEvent.type.HasFlag(GpmNativeBindings.GpmEventType.GPM_MOVE) ||
                     gpmEvent.type.HasFlag(GpmNativeBindings.GpmEventType.GPM_DRAG))
            {
                RaiseMouseEvent(RawPointerEventType.Move, point, null, modifiers);
            }

            // Handle click events (single, double, triple)
            if (gpmEvent.type.HasFlag(GpmNativeBindings.GpmEventType.GPM_SINGLE) ||
                gpmEvent.type.HasFlag(GpmNativeBindings.GpmEventType.GPM_DOUBLE) ||
                gpmEvent.type.HasFlag(GpmNativeBindings.GpmEventType.GPM_TRIPLE))
            {
                // Clicks are already handled by DOWN/UP events
                // Just track for future reference if needed
            }

            // Handle wheel events
            if (buttons.HasFlag(GpmNativeBindings.GpmButtons.GPM_B_UP))
            {
                RaiseMouseEvent(RawPointerEventType.Wheel, point, new Vector(0, 1), modifiers);
            }
            else if (buttons.HasFlag(GpmNativeBindings.GpmButtons.GPM_B_DOWN))
            {
                RaiseMouseEvent(RawPointerEventType.Wheel, point, new Vector(0, -1), modifiers);
            }

            _lastButtonState = buttons;
        }

        private void ProcessButtonDown(GpmNativeBindings.GpmButtons buttons, Point point, RawInputModifiers modifiers)
        {
            if (buttons.HasFlag(GpmNativeBindings.GpmButtons.GPM_B_LEFT) &&
                !_lastButtonState.HasFlag(GpmNativeBindings.GpmButtons.GPM_B_LEFT))
            {
                RaiseMouseEvent(RawPointerEventType.LeftButtonDown, point, null, modifiers);
            }

            if (buttons.HasFlag(GpmNativeBindings.GpmButtons.GPM_B_MIDDLE) &&
                !_lastButtonState.HasFlag(GpmNativeBindings.GpmButtons.GPM_B_MIDDLE))
            {
                RaiseMouseEvent(RawPointerEventType.MiddleButtonDown, point, null, modifiers);
            }

            if (buttons.HasFlag(GpmNativeBindings.GpmButtons.GPM_B_RIGHT) &&
                !_lastButtonState.HasFlag(GpmNativeBindings.GpmButtons.GPM_B_RIGHT))
            {
                RaiseMouseEvent(RawPointerEventType.RightButtonDown, point, null, modifiers);
            }
        }

        private void ProcessButtonUp(GpmNativeBindings.GpmButtons buttons, Point point, RawInputModifiers modifiers)
        {
            // Check for transitions from pressed to released
            if (_lastButtonState.HasFlag(GpmNativeBindings.GpmButtons.GPM_B_LEFT) &&
                !buttons.HasFlag(GpmNativeBindings.GpmButtons.GPM_B_LEFT))
            {
                RaiseMouseEvent(RawPointerEventType.LeftButtonUp, point, null, modifiers);
            }

            if (_lastButtonState.HasFlag(GpmNativeBindings.GpmButtons.GPM_B_MIDDLE) &&
                !buttons.HasFlag(GpmNativeBindings.GpmButtons.GPM_B_MIDDLE))
            {
                RaiseMouseEvent(RawPointerEventType.MiddleButtonUp, point, null, modifiers);
            }

            if (_lastButtonState.HasFlag(GpmNativeBindings.GpmButtons.GPM_B_RIGHT) &&
                !buttons.HasFlag(GpmNativeBindings.GpmButtons.GPM_B_RIGHT))
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
            public int tv_sec;
            public int tv_usec;
        }

        [DllImport("libc", SetLastError = true)]
        private static extern int Select(int nfds, IntPtr readfds, IntPtr writefds, IntPtr exceptfds, ref Timeval timeout);

        #endregion
    }
}
