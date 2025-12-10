using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Threading;
using Consolonia.Controls;
using Consolonia.Core.Drawing.PixelBufferImplementation;
using Consolonia.Core.Infrastructure;

namespace Consolonia.NUnit
{
    public sealed class UnitTestConsole : IConsole
    {
        private PixelBufferCoordinate _fakeCaretPosition;
        private ConsoloniaLifetime _lifetime;

        public UnitTestConsole(PixelBufferSize size)
        {
            Size = size;
            PixelBuffer = new PixelBuffer(size.Width, size.Height);
        }

        // ReSharper disable once MemberCanBePrivate.Global
        public PixelBuffer PixelBuffer { get; }

        public PixelBufferSize Size { get; set; }

        public bool SupportsComplexEmoji => true;

        public bool SupportsAltSolo => true;
        public bool SupportsMouse => false;
        public bool SupportsMouseMove => false;
        public bool ShowMouseCursor => false;

        public void SetTitle(string title)
        {
            Console.WriteLine($"Title changed to {title}");
        }

        public void SetCaretPosition(PixelBufferCoordinate bufferPoint)
        {
            _fakeCaretPosition = bufferPoint;
        }

        public PixelBufferCoordinate GetCaretPosition()
        {
            return _fakeCaretPosition;
        }

        public void WritePixel(PixelBufferCoordinate position, in Pixel pixel)
        {
            if (pixel.CaretStyle != CaretStyle.None)
                // we don't render caret in unit test console
                PixelBuffer[position] = new Pixel(pixel.Foreground, pixel.Background);
            else
                PixelBuffer[position] = pixel;
        }

        public void WriteText(string str)
        {
            // ignore
        }

        public void PauseIO(Task task)
        {
            throw new NotSupportedException();
        }


        public void SetCaretStyle(CaretStyle caretStyle)
        {
        }

        public void HideCaret()
        {
        }

        public void ShowCaret()
        {
        }

        public void PrepareConsole()
        {
        }

        public void RestoreConsole()
        {
        }

        public void ClearScreen()
        {
        }

        public void Flush()
        {
        }

        public void Dispose()
        {
            _lifetime = null;
        }

        public void ClearOutput()
        {
            // screen of the unit test console is always clear, we are writing only to the buffer
            Resized?.Invoke();
        }


        public async Task StringInput(string input)
        {
            foreach (char c in input)
            {
                const Key key = Key.None;
                ulong timestamp = (ulong)Environment.TickCount64;
                // todo: check why Yield is not enough: https://github.com/consolonia/consolonia/runs/7055199426?check_suite_focus=true
                const ulong interval = 50;
                KeyEvent?.Invoke(key, c, RawInputModifiers.None, true, timestamp, true);
                await Task.Delay((int)interval).ConfigureAwait(false);
                KeyEvent?.Invoke(key, c, RawInputModifiers.None, false, timestamp + interval, true);
                await Task.Delay((int)interval).ConfigureAwait(false);
            }

            await WaitRendered().ConfigureAwait(true);
        }

        public async Task WaitRendered()
        {
            await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                _lifetime.MainWindow!.InvalidateVisual();
                await _lifetime.MainWindow.PlatformImpl!.Compositor!.RequestCompositionBatchCommitAsync().Rendered
                    .ConfigureAwait(true);
                await _lifetime.MainWindow.PlatformImpl!.Compositor!.RequestCompositionBatchCommitAsync().Processed
                    .ConfigureAwait(true);
            }, DispatcherPriority.Default).ConfigureAwait(true);

            await Task.Run(async () =>
            {
                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Default);
            });
        }

        public async Task KeyInput(params Key[] keys)
        {
            foreach (Key key in keys) await KeyInput(key).ConfigureAwait(true);
        }

        public async Task KeyInput(int repeat, Key key, RawInputModifiers modifiers = RawInputModifiers.None)
        {
            foreach (int _ in Enumerable.Range(0, repeat)) await KeyInput(key, modifiers).ConfigureAwait(true);
        }

        public async Task KeyInput(Key key, RawInputModifiers modifiers = RawInputModifiers.None)
        {
            ulong timestamp = (ulong)Environment.TickCount64;
            KeyEvent?.Invoke(key, char.MinValue /*will be skipped as control character*/, modifiers, true, timestamp,
                true);
            await Task.Yield();
            KeyEvent?.Invoke(key, char.MinValue /*will be skipped as control character*/, modifiers, false,
                timestamp + 1, true);
            await Task.Yield();

            await WaitRendered().ConfigureAwait(true);
        }


        public void SetupLifetime(ConsoloniaLifetime lifetime)
        {
            _lifetime = lifetime;
        }


#pragma warning disable CS0067
        public event Action Resized;
        public event Action<Key, char, RawInputModifiers, bool, ulong, bool> KeyEvent;
        public event Action<RawPointerEventType, Point, Vector?, RawInputModifiers> MouseEvent;
        public event Action<bool> FocusEvent;
        public event Action<string, ulong, CanBeHandledEventArgs> TextInputEvent;
#pragma warning restore CS0067
    }
}