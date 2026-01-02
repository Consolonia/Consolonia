#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Platform;
using Consolonia.Core.Drawing.PixelBufferImplementation;
using Consolonia.Core.Infrastructure;

namespace Consolonia.Core.Drawing
{
    internal class RenderTarget : IDrawingContextLayerImpl
    {
        private readonly ConsoleWindowImpl _consoleWindowImpl;
        private readonly IConsoleDeviceRenderer _renderer;

        // cache of pixels written so we can ignore them if unchanged.

        internal RenderTarget(ConsoleWindowImpl consoleWindowImpl)
        {
            IConsole console = AvaloniaLocator.Current.GetRequiredService<IConsole>();

            _consoleWindowImpl = consoleWindowImpl;
            _renderer = console.CreateConsoleRenderer(_consoleWindowImpl);
        }

        public void Dispose()
        {
        }

        public void Save(string fileName, int? quality = null)
        {
            throw new NotImplementedException();
        }

        public void Save(Stream stream, int? quality = null)
        {
            throw new NotImplementedException();
        }

        public Vector Dpi { get; } = Vector.One;
        public PixelSize PixelSize { get; } = new(1, 1);
        public int Version => 0;

        void IDrawingContextLayerImpl.Blit(IDrawingContextImpl context)
        {
            try
            {
                _renderer.RenderToDevice();
            }
            catch (InvalidDrawingContextException)
            {
            }
        }

        bool IDrawingContextLayerImpl.CanBlit => true;

        public bool IsCorrupted => false;

        public IDrawingContextImpl CreateDrawingContext(bool useScaledDrawing)
        {
            if (useScaledDrawing)
                throw new NotImplementedException("Consolonia doesn't support useScaledDrawing");
            return new DrawingContextImpl(_consoleWindowImpl);
        }
    }
}