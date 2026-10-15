using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Threading;
using Consolonia.Controls;
using Consolonia.Core.Drawing.PixelBufferImplementation;
using Consolonia.Core.Helpers;
using Consolonia.Core.Text;

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

        protected ConsoleBase(IConsoleOutput consoleOutput)
        {
            if (consoleOutput is ConsoleBase)
                throw new ArgumentException("ConsoleBase cannot be used as a console output", nameof(consoleOutput));

            Console.TreatControlCAsInput = true;

            _consoleOutput = consoleOutput;

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

        /// <inheritdoc />
        public IReadOnlySet<string> SupportedPointerShapes { get; protected set; } = new HashSet<string>();

        /// <summary>
        ///     True when the terminal takes X11 cursor names (xterm) rather than CSS ones; the shapes
        ///     in <see cref="SupportedPointerShapes" /> are CSS names either way.
        /// </summary>
        protected bool PointerShapesUseX11Names { get; set; }

        /// <inheritdoc />
        public virtual void SetPointerShape(string shape)
        {
            string name = PointerShapesUseX11Names ? PointerShapes.ToX11(shape) : shape;
            if (name == null)
                return;

            WriteText(Esc.SetPointerShape(name));
            Flush();
        }

        /// <summary>
        ///     Works out which mouse pointer shapes the terminal can draw itself (OSC 22), so the
        ///     pointer can be the terminal's own rather than a character.
        /// </summary>
        /// <param name="readAnswersUntilDeviceAttributes">
        ///     Reads whatever the terminal sends back, up to and including its Device Attributes
        ///     answer, or all it sent before going quiet. How input is read is the one part of the
        ///     probe that differs between consoles.
        /// </param>
        /// <remarks>
        ///     <para>
        ///         Kitty's support query names every shape an Avalonia cursor maps to, and is followed
        ///         by XTVERSION and then a Device Attributes request as a sentinel, which every
        ///         terminal answers. Everything up to the sentinel's answer is read, and what it means
        ///         is decided by <see cref="PointerShapes.Detect" />. A terminal that neither answers
        ///         nor is recognised keeps the character pointer.
        ///     </para>
        ///     <para>
        ///         The support query goes out in several parts rather than one because libtsm, and so
        ///         kmscon, keeps only the first 127 bytes of an OSC string and silently drops the rest:
        ///         a single query naming every shape was 186 bytes, and the names past the cut went
        ///         unanswered and were taken as unsupported.
        ///     </para>
        /// </remarks>
        protected void TryToSupportPointerShapes(Func<string> readAnswersUntilDeviceAttributes)
        {
            ArgumentNullException.ThrowIfNull(readAnswersUntilDeviceAttributes);

            foreach (IReadOnlyList<string> batch in PointerShapes.QueryBatches)
                WriteText(Esc.QueryPointerShapes(batch));
            WriteText(Esc.QueryTerminalVersion);
            WriteText("\u001b[c"); // sentinel: Device Attributes query
            Flush(); // the queries must actually reach the terminal, otherwise it never responds

            (IReadOnlySet<string> supported, bool usesX11Names) =
                PointerShapes.Detect(readAnswersUntilDeviceAttributes(), Environment.GetEnvironmentVariable);
            SupportedPointerShapes = supported;
            PointerShapesUseX11Names = usesX11Names;
        }

        public virtual void ShowCaret()
        {
            _consoleOutput.ShowCaret();
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