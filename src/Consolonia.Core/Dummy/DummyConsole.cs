using Consolonia.Controls;
using Consolonia.Core.Drawing.PixelBufferImplementation;
using Consolonia.Core.Infrastructure;

namespace Consolonia.Core.Dummy
{
    public class DummyConsole : ConsoleBase
    {
        public DummyConsole()
            : this(80, 25)
        {
        }

        public DummyConsole(ushort width, ushort height)
            : base(new DummyConsoleOutput(width, height))
        {
        }

        public override bool SupportsAltSolo => false;

        public override bool SupportsMouse => false;

        public override bool SupportsMouseMove => false;

        public override bool ShowMouseCursor => false;
    }

    public class DummyConsoleOutput : IConsoleOutput
    {
        private PixelBufferCoordinate _caretPosition = new(0, 0);
        private bool _disposed;

        public DummyConsoleOutput()
            : this(80, 25)
        {
        }

        public DummyConsoleOutput(ushort width, ushort height)
        {
            Size = new PixelBufferSize(width, height);
        }

        public PixelBufferSize Size { get; set; }

        public bool SupportsComplexEmoji => true;


        public PixelBufferCoordinate GetCaretPosition()
        {
            return _caretPosition;
        }


        public void WritePixel(PixelBufferCoordinate position, in Pixel pixel)
        {
        }

        public void SetCaretPosition(PixelBufferCoordinate bufferPoint)
        {
            _caretPosition = bufferPoint;
        }

        public void SetTitle(string title)
        {
        }

        public void WriteText(string str)
        {
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

        public void ClearOutput()
        {
            SetCaretPosition(new PixelBufferCoordinate(0, 0));
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    // Dispose managed resources here.
                }

                // Dispose unmanaged resources here.

                _disposed = true;
            }
        }
    }
}