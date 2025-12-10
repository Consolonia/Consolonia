using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Threading;
using Consolonia.Controls;
using Consolonia.Core.Drawing.PixelBufferImplementation;
using Consolonia.Core.Helpers;

namespace Consolonia.Core.Infrastructure
{
    /// <summary>
    ///     Base IConsole implementation
    /// </summary>
    /// <remarks>
    ///     This implements disposable and eventing for IConsoleInput and
    ///     wraps around internal IConsoleOutput
    /// </remarks>
    public abstract class ConsoleBase : IConsole, IDisposable
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

        protected Task PauseTask { get; private set; }

        /// <summary>
        ///     Pause the IO
        /// </summary>
        /// <param name="task"></param>
        public virtual void PauseIO(Task task)
        {
            task.ContinueWith(_ => { PauseTask = null; }, TaskScheduler.Default);
            PauseTask = task;
        }

        protected void StartSizeCheckTimerAsync(uint slowInterval = 1500)
        {
            Task.Run(async () =>
            {
                await Helper.WaitDispatcherInitialized();

                while (!Disposed)
                {
                    Task pauseTask = PauseTask;
                    if (pauseTask != null)
                        await pauseTask;


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
            await Dispatcher.UIThread.InvokeAsync(action, DispatcherPriority.Input);
        }

        #region IConsoleInput

        public abstract bool SupportsMouse { get; }

        public abstract bool SupportsMouseMove { get; }

        public event Action<Key, char, RawInputModifiers, bool, ulong, bool> KeyEvent;
        public event Action<RawPointerEventType, Point, Vector?, RawInputModifiers> MouseEvent;
        public event Action<bool> FocusEvent;
        public event Action<string, ulong, CanBeHandledEventArgs> TextInputEvent;

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
            get => _consoleOutput.Size;
            set
            {
                // ReSharper disable once UsageOfDefaultStructEquality //todo: low use special equality interfaces
                if (_consoleOutput.Size.Equals(value))
                    return;

                _consoleOutput.Size = value;
                Resized?.Invoke();
            }
        }

        public virtual bool SupportsComplexEmoji => _consoleOutput.SupportsComplexEmoji;

        public abstract bool SupportsAltSolo { get; }

        public abstract bool ShowMouseCursor { get; }


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

        public virtual void PrepareConsole()
        {
            _consoleOutput.PrepareConsole();
        }

        public virtual void WritePixel(PixelBufferCoordinate position, in Pixel pixel)
        {
            _consoleOutput.WritePixel(position, in pixel);
        }

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

        public virtual void WriteText(string str)
        {
            PauseTask?.Wait();
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