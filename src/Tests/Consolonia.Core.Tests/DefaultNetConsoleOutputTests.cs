using System;
using System.IO;
using Avalonia.Media;
using Consolonia.Core.Drawing.PixelBufferImplementation;
using Consolonia.Core.Infrastructure;
using NUnit.Framework;

namespace Consolonia.Core.Tests
{
    [TestFixture]
    [NonParallelizable]
    public class DefaultNetConsoleOutputTests
    {
        [Test]
        public void WritePixelSkipsZeroWidthPlaceholderWithoutCorruptingOutput()
        {
            var output = new TestDefaultNetConsoleOutput();
            TextWriter originalWriter = Console.Out;
            using var writer = new StringWriter();

            try
            {
                Console.SetOut(writer);

                output.WritePixel(new PixelBufferCoordinate(0, 0), new Pixel(new Symbol('您', 2), Colors.Transparent));
                output.WritePixel(new PixelBufferCoordinate(1, 0),
                    new Pixel(PixelForeground.Empty, PixelBackground.Transparent));
                output.WritePixel(new PixelBufferCoordinate(2, 0), new Pixel(new Symbol('正', 2), Colors.Transparent));
                output.Flush();
            }
            finally
            {
                Console.SetOut(originalWriter);
            }

            Assert.That(writer.ToString(), Is.EqualTo("您正"));
            Assert.That(output.SetCaretPositionCallCount, Is.EqualTo(0));
        }

        private sealed class TestDefaultNetConsoleOutput : DefaultNetConsoleOutput
        {
            public int SetCaretPositionCallCount { get; private set; }

            public override void SetCaretPosition(PixelBufferCoordinate bufferPoint)
            {
                SetCaretPositionCallCount++;
            }
        }
    }
}