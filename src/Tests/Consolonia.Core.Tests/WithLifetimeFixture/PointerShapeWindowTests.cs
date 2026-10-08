using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Consolonia.Controls;
using Consolonia.Core.Drawing;
using Consolonia.Core.Drawing.PixelBufferImplementation;
using Consolonia.Core.Infrastructure;
using Consolonia.Core.Text;
using NUnit.Framework;

namespace Consolonia.Core.Tests.WithLifetimeFixture
{
    [TestFixture]
    public class PointerShapeWindowTests
    {
        [Test]
        public void LeavingAShapeResetsTheTerminalEvenWithoutDefault()
        {
            // A terminal that answered the support query with "text" but not "default".
            var console = new PointerShapeConsole(new HashSet<string> { "text" });

            // Swapped in for this window only: a nested locator scope would hide the rendering
            // services the lifetime's render loop goes on resolving.
            var original = AvaloniaLocator.Current.GetRequiredService<IConsole>();
            AvaloniaLocator.CurrentMutable.Bind<IConsole>().ToConstant(console);
            try
            {
                using var window = new ConsoleWindowImpl();

                window.SetCursor(new CursorImpl(StandardCursorType.Ibeam));
                window.SetCursor(new CursorImpl(StandardCursorType.Arrow));
                window.SetCursor(new CursorImpl(StandardCursorType.Arrow));
            }
            finally
            {
                AvaloniaLocator.CurrentMutable.Bind<IConsole>().ToConstant(original);
            }

            Assert.That(console.Written, Is.EqualTo(new[] { "shape:text", Esc.ResetPointerShape }),
                "the I-beam is set, then taken off once for the arrow, which the terminal does not have");
        }

        private sealed class PointerShapeConsole(IReadOnlySet<string> supported) : IConsole
        {
            public List<string> Written { get; } = [];

            public ConsoleCapabilities Capabilities => ConsoleCapabilities.SupportsAltSolo;
            public PixelBufferSize Size { get; set; } = new(80, 25);
            public int CellPixelWidth => 8;
            public int CellPixelHeight => 16;
            public IReadOnlySet<string> SupportedPointerShapes => supported;

            public void SetPointerShape(string shape)
            {
                Written.Add("shape:" + shape);
            }

            public void WriteText(string str)
            {
                Written.Add(str);
            }

            public void SetTitle(string title)
            {
            }

            public void SetCaretPosition(PixelBufferCoordinate bufferPoint)
            {
            }

            public PixelBufferCoordinate GetCaretPosition()
            {
                return default;
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

            public void WritePixel(PixelBufferCoordinate position, in Pixel pixel)
            {
            }

            public void WriteSixel(PixelBufferCoordinate position, Sixel sixel)
            {
            }

            public void Flush()
            {
            }

            public void PauseIO(Task task)
            {
            }

            public void StartInputLoop()
            {
            }

#pragma warning disable CS0067 // never raised: the test drives the window directly
            public event Action Resized;
            public event Action<Key, char, RawInputModifiers, bool, ulong, bool> KeyEvent;
            public event Action<string, ulong, CanBeHandledEventArgs> TextInputEvent;
            public event Action<RawPointerEventType, Point, Vector?, RawInputModifiers> MouseEvent;
            public event Action<bool> FocusEvent;
#pragma warning restore CS0067
        }
    }
}