using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
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
using Key = Terminal.Gui.Key;

namespace Consolonia.PlatformSupport
{
    /// <summary>
    ///     EvDev-based Console implementation that monitors evdev for direct mouse input
    ///     (especially scroll wheel events in pure TTY environments) while using curses 
    ///     for keyboard input and rendering.
    /// </summary>
    public class EvdevConsole : CursesConsole
    {
        private readonly CancellationTokenSource _evdevCancellation;
        private readonly List<EvdevMouseWrapper> _mouseDevices;
        private Point _mousePosition = new(0, 0);
        private RawInputModifiers _moveModifiers = RawInputModifiers.None;
        private bool _leftButtonPressed;
        private bool _rightButtonPressed;
        private bool _middleButtonPressed;

        public EvdevConsole()
        {
            _evdevCancellation = new CancellationTokenSource();
            _mouseDevices = new List<EvdevMouseWrapper>();
            
            // Initialize evdev mouse monitoring
            InitializeEvdevMice();
            StartEvdevMouseLoop();
        }

        private void InitializeEvdevMice()
        {
            try
            {
                string inputDir = "/dev/input";
                if (!Directory.Exists(inputDir))
                {
                    System.Diagnostics.Debug.WriteLine("EvDev: /dev/input directory not found");
                    return;
                }

                var eventFiles = Directory.GetFiles(inputDir, "event*")
                    .OrderBy(f => f)
                    .ToArray();
                
                foreach (var eventFile in eventFiles)
                {
                    try
                    {
                        var device = new EvdevMouseWrapper(eventFile);
                        
                        if (device.IsMouse)
                        {
                            device.Grab();
                            _mouseDevices.Add(device);
                            System.Diagnostics.Debug.WriteLine($"EvDev: Monitoring {device.Name} ({eventFile})");
                        }
                        else
                        {
                            device.Dispose();
                        }
                    }
                    catch (UnauthorizedAccessException)
                    {
                        System.Diagnostics.Debug.WriteLine($"EvDev: Access denied to {eventFile} (run with sudo or add user to input group)");
                    }
#pragma warning disable CA1031 // Intentionally catching all exceptions during device enumeration
                    catch (Exception ex)
#pragma warning restore CA1031
                    {
                        System.Diagnostics.Debug.WriteLine($"EvDev: Failed to open {eventFile}: {ex.Message}");
                    }
                }
                
                if (_mouseDevices.Count == 0)
                {
                    System.Diagnostics.Debug.WriteLine("EvDev: No mouse devices found, using curses mouse events");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"EvDev: Monitoring {_mouseDevices.Count} mouse device(s) for scroll wheel support");
                }
            }
#pragma warning disable CA1031 // Intentionally catching all exceptions to fail gracefully
            catch (Exception ex)
#pragma warning restore CA1031
            {
                System.Diagnostics.Debug.WriteLine($"EvDev: Initialization failed: {ex.Message}");
            }
        }

        private void StartEvdevMouseLoop()
        {
            if (_mouseDevices.Count == 0)
                return;

            Task.Run(async () =>
            {
                await Helper.WaitDispatcherInitialized();

                while (!Disposed && !_evdevCancellation.Token.IsCancellationRequested)
                {
                    try
                    {
                        foreach (var device in _mouseDevices.ToArray())
                        {
                            if (_evdevCancellation.Token.IsCancellationRequested)
                                break;

                            try
                            {
                                var evt = device.NextEvent();
                                if (evt != null)
                                {
                                    await DispatchInputAsync(() => HandleEvdevEvent(evt));
                                }
                            }
#pragma warning disable CA1031 // Intentionally catching per-device exceptions
                            catch (Exception ex)
#pragma warning restore CA1031
                            {
                                System.Diagnostics.Debug.WriteLine($"EvDev: Error reading from {device.Name}: {ex.Message}");
                            }
                        }

                        await Task.Delay(1, _evdevCancellation.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
#pragma warning disable CA1031 // Intentionally catching all loop exceptions
                    catch (Exception ex)
#pragma warning restore CA1031
                    {
                        System.Diagnostics.Debug.WriteLine($"EvDev: Mouse loop error: {ex.Message}");
                    }
                }
            }, _evdevCancellation.Token);
        }

        private void HandleEvdevEvent(EvdevEventWrapper evt)
        {
            ulong timestamp = (ulong)Environment.TickCount64;

            // Event types from linux/input-event-codes.h
            const ushort EV_SYN = 0x00;
            const ushort EV_KEY = 0x01;
            const ushort EV_REL = 0x02;
            const ushort EV_ABS = 0x03;

            const ushort REL_X = 0x00;
            const ushort REL_Y = 0x01;
            const ushort REL_WHEEL = 0x08;
            const ushort REL_HWHEEL = 0x06;

            const ushort BTN_LEFT = 0x110;
            const ushort BTN_RIGHT = 0x111;
            const ushort BTN_MIDDLE = 0x112;

            switch (evt.Type)
            {
                case EV_REL:
                    switch (evt.Code)
                    {
                        case REL_X:
                            _mousePosition = new Point(
                                Math.Clamp(_mousePosition.X + evt.Value, 0, Size.Width - 1),
                                _mousePosition.Y);
                            RaiseMouseEvent(RawPointerEventType.Move, _mousePosition, null, _moveModifiers);
                            break;

                        case REL_Y:
                            _mousePosition = new Point(
                                _mousePosition.X,
                                Math.Clamp(_mousePosition.Y + evt.Value, 0, Size.Height - 1));
                            RaiseMouseEvent(RawPointerEventType.Move, _mousePosition, null, _moveModifiers);
                            break;

                        case REL_WHEEL:
                            // THIS IS THE CRITICAL PART - scroll wheel events in pure TTY!
                            var wheelDelta = new Vector(0, evt.Value > 0 ? 1 : -1);
                            RaiseMouseEvent(RawPointerEventType.Wheel, _mousePosition, wheelDelta, _moveModifiers);
                            break;

                        case REL_HWHEEL:
                            var hWheelDelta = new Vector(evt.Value > 0 ? 1 : -1, 0);
                            RaiseMouseEvent(RawPointerEventType.Wheel, _mousePosition, hWheelDelta, _moveModifiers);
                            break;
                    }
                    break;

                case EV_KEY:
                    bool pressed = evt.Value == 1;
                    bool released = evt.Value == 0;

                    switch (evt.Code)
                    {
                        case BTN_LEFT:
                            if (pressed && !_leftButtonPressed)
                            {
                                _leftButtonPressed = true;
                                _moveModifiers |= RawInputModifiers.LeftMouseButton;
                                RaiseMouseEvent(RawPointerEventType.LeftButtonDown, _mousePosition, null, _moveModifiers);
                            }
                            else if (released && _leftButtonPressed)
                            {
                                _leftButtonPressed = false;
                                _moveModifiers &= ~RawInputModifiers.LeftMouseButton;
                                RaiseMouseEvent(RawPointerEventType.LeftButtonUp, _mousePosition, null, _moveModifiers);
                            }
                            break;

                        case BTN_RIGHT:
                            if (pressed && !_rightButtonPressed)
                            {
                                _rightButtonPressed = true;
                                _moveModifiers |= RawInputModifiers.RightMouseButton;
                                RaiseMouseEvent(RawPointerEventType.RightButtonDown, _mousePosition, null, _moveModifiers);
                            }
                            else if (released && _rightButtonPressed)
                            {
                                _rightButtonPressed = false;
                                _moveModifiers &= ~RawInputModifiers.RightMouseButton;
                                RaiseMouseEvent(RawPointerEventType.RightButtonUp, _mousePosition, null, _moveModifiers);
                            }
                            break;

                        case BTN_MIDDLE:
                            if (pressed && !_middleButtonPressed)
                            {
                                _middleButtonPressed = true;
                                _moveModifiers |= RawInputModifiers.MiddleMouseButton;
                                RaiseMouseEvent(RawPointerEventType.MiddleButtonDown, _mousePosition, null, _moveModifiers);
                            }
                            else if (released && _middleButtonPressed)
                            {
                                _middleButtonPressed = false;
                                _moveModifiers &= ~RawInputModifiers.MiddleMouseButton;
                                RaiseMouseEvent(RawPointerEventType.MiddleButtonUp, _mousePosition, null, _moveModifiers);
                            }
                            break;
                    }
                    break;

                case EV_SYN:
                    // Sync events - batch boundary
                    break;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _evdevCancellation.Cancel();

                foreach (var device in _mouseDevices)
                {
#pragma warning disable CA1031 // Intentionally ignoring disposal exceptions
                    try
                    {
                        device.Ungrab();
                        device.Dispose();
                    }
                    catch
                    {
                        // Ignore disposal errors
                    }
#pragma warning restore CA1031
                }

                _mouseDevices.Clear();
                _evdevCancellation.Dispose();
            }

            base.Dispose(disposing);
        }

        #region EvDev Wrapper (using reflection to access EvDevSharpWrapper internals)

        /// <summary>
        /// Wrapper around EvDevSharpWrapper's internal Device class using reflection
        /// </summary>
        private class EvdevMouseWrapper : IDisposable
        {
            private readonly object _device;
            private readonly Type _deviceType;
            private readonly MethodInfo _nextEventMethod;
            private readonly MethodInfo _grabMethod;
            private readonly MethodInfo _ungrabMethod;
            private readonly PropertyInfo _nameProperty;

            public EvdevMouseWrapper(string path)
            {
                var assembly = Assembly.Load("EvDevSharp");
                _deviceType = assembly.GetType("EvDevSharp.Device") 
                             ?? throw new TypeLoadException("Could not load EvDevSharp.Device");

                var constructor = _deviceType.GetConstructor([typeof(string)]);
                _device = constructor?.Invoke([path]) 
                         ?? throw new InvalidOperationException("Could not create Device");

                _nextEventMethod = _deviceType.GetMethod("NextEvent");
                _grabMethod = _deviceType.GetMethod("Grab");
                _ungrabMethod = _deviceType.GetMethod("Ungrab");
                _nameProperty = _deviceType.GetProperty("Name");
            }

            public string Name => (string)_nameProperty.GetValue(_device);

            public bool IsMouse
            {
                get
                {
                    string name = Name.ToLowerInvariant();
                    return name.Contains("mouse") || 
                           name.Contains("touchpad") ||
                           name.Contains("pointer") ||
                           name.Contains("trackpoint");
                }
            }

            public EvdevEventWrapper NextEvent()
            {
                var evt = _nextEventMethod.Invoke(_device, null);
                return evt != null ? new EvdevEventWrapper(evt) : null;
            }

            public void Grab()
            {
                _grabMethod.Invoke(_device, null);
            }

            public void Ungrab()
            {
                _ungrabMethod.Invoke(_device, null);
            }

            public void Dispose()
            {
                if (_device is IDisposable disposable)
                    disposable.Dispose();
            }
        }

        /// <summary>
        /// Wrapper around EvDevSharpWrapper's internal Event struct using reflection
        /// </summary>
        private class EvdevEventWrapper
        {
            private readonly object _event;
            private readonly FieldInfo _typeField;
            private readonly FieldInfo _codeField;
            private readonly FieldInfo _valueField;

            public EvdevEventWrapper(object evt)
            {
                _event = evt;
                var eventType = evt.GetType();
                _typeField = eventType.GetField("Type");
                _codeField = eventType.GetField("Code");
                _valueField = eventType.GetField("Value");
            }

            public ushort Type => (ushort)_typeField.GetValue(_event);
            public ushort Code => (ushort)_codeField.GetValue(_event);
            public int Value => (int)_valueField.GetValue(_event);
        }

        #endregion
    }
}