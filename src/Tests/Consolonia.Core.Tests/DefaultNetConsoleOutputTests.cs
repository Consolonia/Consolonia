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

        [Test]
        public void SetCaretPositionWritesBufferedTextBeforeMovingTheCursor()
        {
            var output = new CursorRecordingNetConsoleOutput();
            TextWriter originalWriter = Console.Out;
            using var writer = new StringWriter();

            try
            {
                Console.SetOut(writer);

                output.WritePixel(new PixelBufferCoordinate(2, 1), new Pixel(new Symbol('a', 1), Colors.Transparent));
                output.WritePixel(new PixelBufferCoordinate(3, 1), new Pixel(new Symbol('b', 1), Colors.Transparent));
                // the render target places the caret before the frame's last Flush
                output.SetCaretPosition(new PixelBufferCoordinate(5, 3));
                output.Flush();
            }
            finally
            {
                Console.SetOut(originalWriter);
            }

            // "ab" goes out at (2,1) before the cursor moves to the caret, not after it
            Assert.That(writer.ToString(), Is.EqualTo("[2,1]ab[5,3]"));
        }

        [Test]
        public void WritePixelAtTheCaretPositionDoesNotMoveTheCursorAgain()
        {
            var output = new CursorRecordingNetConsoleOutput();
            TextWriter originalWriter = Console.Out;
            using var writer = new StringWriter();

            try
            {
                Console.SetOut(writer);

                output.SetCaretPosition(new PixelBufferCoordinate(5, 3));
                output.WritePixel(new PixelBufferCoordinate(5, 3), new Pixel(new Symbol('a', 1), Colors.Transparent));
                output.Flush();
            }
            finally
            {
                Console.SetOut(originalWriter);
            }

            Assert.That(writer.ToString(), Is.EqualTo("[5,3]a"));
        }

        /// <summary>Writes each cursor move to the output as "[x,y]", in order with the text.</summary>
        private sealed class CursorRecordingNetConsoleOutput : DefaultNetConsoleOutput
        {
            internal override void MoveCursor(PixelBufferCoordinate bufferPoint)
            {
                Console.Write($"[{bufferPoint.X},{bufferPoint.Y}]");
            }
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