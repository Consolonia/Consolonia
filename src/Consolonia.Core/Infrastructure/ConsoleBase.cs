using System;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Threading;
using Consolonia.Controls;
using Consolonia.Core.Drawing;
using Consolonia.Core.Drawing.PixelBufferImplementation;
using Consolonia.Core.Helpers;

namespace Consolonia.Core.Infrastructure
{
    /// <summary>
    ///     Base IConsole implementation
    /// </summary>
    /// <remarks>
    ///     This implements disposable and eventing for IConsoleInput and
    ///     wraps around internal IConsoleOutput.
    ///     Thread-safe
    /// </remarks>
    public abstract class ConsoleBase : PauseBase, IConsole, IDisposable
    {
        private readonly IConsoleOutput _consoleOutput;

        /// <summary>The cell size the terminal reported, unrounded; empty when it reported none.</summary>
        private Avalonia.Size _reportedCellPixelSize;

        protected ConsoleBase(IConsoleOutput consoleOutput)
        {
            if (consoleOutput is ConsoleBase)
                throw new ArgumentException("ConsoleBase cannot be used as a console output", nameof(consoleOutput));

            Console.TreatControlCAsInput = true;

            _consoleOutput = consoleOutput;

            if (consoleOutput is AnsiConsoleOutput ansiConsoleOutput)
            {
                ansiConsoleOutput.GetConsoleCellSizeHandler = GetConsoleCellSize;
                ansiConsoleOutput.RequestAnsiResponseHandler = RequestAnsiResponse;
            }

            Size = consoleOutput.Size;
        }

        protected bool Disposed { get; private set; }

        public override void PauseIO(Task task)
        {
            base.PauseIO(task);
            _consoleOutput.PauseIO(task);
        }

        protected void StartSizeCheckTimerAsync(uint slowInterval = 1500)
        {
            Task.Run(async () =>
            {
                await Helper.WaitDispatcherInitialized();

                while (!Disposed)
                {
                    await WaitPauseTaskIfNecessaryAsync();


                    int timeout = -1;
                    await DispatchInputAsync(() => { timeout = (int)(CheckSize() ? 1 : slowInterval); });
                    await Task.Delay(timeout);
                }
            }); //todo: we should rethrow in main thread, or may be we should keep the loop running, but raise some general handler if it already exists, like Dispatcher.UnhandledException or whatever + check other places we use Task.Run and async void
        }


#pragma warning disable CA1822 // todo: low is it legit to invoke static Dispatcher, do we have instance somehwere available?
        // ReSharper disable once MemberCanBeMadeStatic.Global
        protected async Task DispatchInputAsync(Action action)
#pragma warning restore CA1822
        {
            //todo: key and mouse input now can be dispatched on any thread (from avalonia 12)
            await Dispatcher.UIThread.InvokeAsync(action, DispatcherPriority.Input);
        }

        #region IConsoleInput

        public event Action<Key, char, RawInputModifiers, bool, ulong, bool> KeyEvent;
        public event Action<RawPointerEventType, Point, Vector?, RawInputModifiers> MouseEvent;
        public event Action<bool> FocusEvent;
        public event Action<string, ulong, CanBeHandledEventArgs> TextInputEvent;
        public abstract void StartInputLoop();

        protected void RaiseMouseEvent(RawPointerEventType eventType, Point point, Vector? wheelDelta,
            RawInputModifiers modifiers)
        {
            // System.Diagnostics.Debug.WriteLine($"Mouse event: {eventType} [{point}] {wheelDelta} {modifiers}");
            MouseEvent?.Invoke(eventType, point, wheelDelta, modifiers);
        }

        protected void RaiseKeyPress(Key key, char character, RawInputModifiers modifiers, bool down, ulong timeStamp,
            bool tryAsTextInput = true)
        {
            KeyEvent?.Invoke(key, character, modifiers, down, timeStamp, tryAsTextInput);
        }

        protected void RaiseTextInput(string text, ulong timestamp, CanBeHandledEventArgs canBeHandledEventArgs = null)
        {
            TextInputEvent?.Invoke(text, timestamp, canBeHandledEventArgs ?? CanBeHandledEventArgs.Default);
        }

        protected void RaiseFocusEvent(bool focused)
        {
            FocusEvent?.Invoke(focused);
        }

        #endregion

        #region IConsoleOutput

        public PixelBufferSize Size
        {
            [MethodImpl(MethodImplOptions.Synchronized)]
            get => _consoleOutput.Size;
            set
            {
                lock (this)
                {
                    // ReSharper disable once UsageOfDefaultStructEquality //todo: low use special equality interfaces
                    if (_consoleOutput.Size.Equals(value))
                        return;

                    _consoleOutput.Size = value;
                }

                Resized?.Invoke();
            }
        }


        public ConsoleCapabilities Capabilities { get; protected set; }

        public int CellPixelWidth => _consoleOutput.CellPixelWidth;

        public int CellPixelHeight => _consoleOutput.CellPixelHeight;

        /// <summary>
        ///     True when the terminal can report the mouse in pixels (SGR-Pixels, DEC private mode 1016)
        ///     and has told us its cell size, without which those pixels cannot be turned back into cells.
        /// </summary>
        protected bool TerminalSupportsPixelMouse =>
            _consoleOutput is AnsiConsoleOutput { SupportsSgrPixelsMouse: true } &&
            _reportedCellPixelSize is { Width: > 0, Height: > 0 };

        /// <summary>
        ///     Turns the position in an SGR-Pixels mouse report (one-based pixels) into a cell position,
        ///     keeping the fraction that says where in the cell the pointer is.
        /// </summary>
        protected Point PixelToCell(int x, int y)
        {
            return new Point(Math.Max(0, x - 1) / _reportedCellPixelSize.Width,
                Math.Max(0, y - 1) / _reportedCellPixelSize.Height);
        }

        public event Action Resized;

        public virtual void ClearScreen()
        {
            _consoleOutput.ClearScreen();
        }

        public virtual PixelBufferCoordinate GetCaretPosition()
        {
            return _consoleOutput.GetCaretPosition();
        }

        public virtual void HideCaret()
        {
            _consoleOutput.HideCaret();
        }

        [MethodImpl(MethodImplOptions.Synchronized)]
        public virtual void PrepareConsole()
        {
            _consoleOutput.PrepareConsole();
            Capabilities |= _consoleOutput.Capabilities;
        }

        public virtual void WritePixel(PixelBufferCoordinate position, in Pixel pixel)
        {
            _consoleOutput.WritePixel(position, in pixel);
        }

        [MethodImpl(MethodImplOptions.Synchronized)]
        public virtual void RestoreConsole()
        {
            _consoleOutput.RestoreConsole();
            _consoleOutput.Flush();
        }

        public virtual void SetCaretPosition(PixelBufferCoordinate bufferPoint)
        {
            _consoleOutput.SetCaretPosition(bufferPoint);
        }

        public virtual void SetCaretStyle(CaretStyle caretStyle)
        {
            _consoleOutput.SetCaretStyle(caretStyle);
        }

        public virtual void SetTitle(string title)
        {
            _consoleOutput.SetTitle(title);
        }

        public virtual void ShowCaret()
        {
            _consoleOutput.ShowCaret();
        }

        public virtual void WriteSixel(PixelBufferCoordinate position, Sixel sixel)
        {
            _consoleOutput.WriteSixel(position, sixel);
        }

        public virtual void WriteText(string str)
        {
            _consoleOutput.WriteText(str);
        }

        protected bool CheckSize()
        {
            if (Size.Width == Console.WindowWidth && Size.Height == Console.WindowHeight) return false;
            Size = new PixelBufferSize((ushort)Console.WindowWidth, (ushort)Console.WindowHeight);
            return true;
        }

        protected virtual (int CellWidth, int CellHeight) GetConsoleCellSize()
        {
            int cols = Console.WindowWidth;
            int rows = Console.WindowHeight;

            string response = RequestAnsiResponse("\x1b[14t", 't', 200);

            int heightPx = 0;
            int widthPx = 0;
            int idx4 = response.IndexOf('4');
            if (idx4 >= 0 && response.EndsWith('t'))
            {
                string inner = response[(idx4 + 1)..^1];
                string[] parts = inner.Split(';', StringSplitOptions.RemoveEmptyEntries);
                // a stray keypress can land in the reply, so a malformed one must fall back, not throw
                if (parts.Length == 2 &&
                    int.TryParse(parts[0], out int parsedHeightPx) &&
                    int.TryParse(parts[1], out int parsedWidthPx))
                {
                    heightPx = parsedHeightPx;
                    widthPx = parsedWidthPx;
                }
            }

            if (widthPx > 0 && heightPx > 0 && cols > 0 && rows > 0)
            {
                _reportedCellPixelSize = new Avalonia.Size((double)widthPx / cols, (double)heightPx / rows);
                return (widthPx / cols, heightPx / rows);
            }

            return (8, 16);
        }

        protected virtual string RequestAnsiResponse(string request, char terminator, int timeoutMs)
        {
            // conhost/ConPTY synthesizes key events from terminal replies and swallows APC ones
            // (the kitty graphics handshake); virtual terminal input passes the reply through raw
            using VirtualTerminalInput.Scope scope = VirtualTerminalInput.Enable();

            WriteText(request);
            Flush();

            var sb = new StringBuilder();
            long deadline = Environment.TickCount64 + timeoutMs;
            while (Environment.TickCount64 < deadline)
            {
                if (!Console.KeyAvailable)
                {
                    // polling without this pins a core for the whole timeout on terminals which never reply
                    Thread.Sleep(1);
                    continue;
                }

                char c = Console.ReadKey(true).KeyChar;
                sb.Append(c);
                if (c == terminator)
                    break;
            }

            return sb.ToString();
        }

        #endregion

        #region IDisposable

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                RestoreConsole();

                Disposed = true;
            }
        }

        [MethodImpl(MethodImplOptions.Synchronized)]
        public void Dispose()
        {
#pragma warning disable CA1063 // Implement IDisposable Correctly
#pragma warning disable CA1303 // Do not pass literals as localized parameters
            Dispose(true);
            GC.SuppressFinalize(this);
#pragma warning restore CA1063 // Implement IDisposable Correctly
#pragma warning restore CA1303 // Do not pass literals as localized parameters
        }

        public void Flush()
        {
            _consoleOutput.Flush();
        }

        #endregion
    }
}