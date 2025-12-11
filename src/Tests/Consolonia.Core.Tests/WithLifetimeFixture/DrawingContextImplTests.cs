// DUPFINDER_ignore

using System;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.Platform;
using Consolonia.Controls;
using Consolonia.Controls.Brushes;
using Consolonia.Core.Drawing;
using Consolonia.Core.Drawing.PixelBufferImplementation;
using Consolonia.Core.Infrastructure;
using NUnit.Framework;

#pragma warning disable CA1305 // Specify IFormatProvider

namespace Consolonia.Core.Tests.WithLifetimeFixture
{
    [TestFixture]
    public class DrawingContextImplTests
    {
        [SetUp]
        public void Setup()
        {
            AvaloniaLocator.CurrentMutable.Bind<IConsoleCapabilities>().ToConstant(new MockConsoleCapabilities
                { Capabilities = ConsoleCapabilities.SupportsComplexEmoji });
        }

        [Test]
        public void BufferInitialized()
        {
            ArgumentNullException.ThrowIfNull(Application.Current.ApplicationLifetime);
            using var consoleTopLevelImpl = new ConsoleWindowImpl();
            PixelBuffer buffer = consoleTopLevelImpl.PixelBuffer;

            for (ushort y = 0; y < buffer.Height; y++)
            for (ushort x = 0; x < buffer.Width; x++)
            {
                Pixel pixel = buffer[x, y];
                Assert.IsTrue(pixel.Width == 1);
                Assert.IsTrue(pixel.Foreground.Symbol.Character == ' ');
                Assert.IsTrue(pixel.Foreground.Color == Colors.Transparent);
                Assert.IsTrue(pixel.Background.Color == Colors.Transparent);
            }
        }

        [Test]
        public void DrawText()
        {
            using var consoleTopLevelImpl = new ConsoleWindowImpl();
            PixelBuffer buffer = consoleTopLevelImpl.PixelBuffer;
            var dc = new DrawingContextImpl(consoleTopLevelImpl);
            for (ushort x = 1; x < 6; x++) DrawText(dc, x, 2, x.ToString(), Brushes.White);
            for (ushort x = 1; x < 6; x++)
            {
                Assert.IsTrue(buffer[x, 2].Width == 1);
                Assert.IsTrue(buffer[x, 2].Foreground.Symbol.Character == x.ToString()[0]);
            }
        }

        [Test]
        public void DrawSingleWide()
        {
            using var consoleTopLevelImpl = new ConsoleWindowImpl();
            PixelBuffer buffer = consoleTopLevelImpl.PixelBuffer;
            var dc = new DrawingContextImpl(consoleTopLevelImpl);
            for (ushort x = 0; x < 10; x++) DrawText(dc, x, 1, x.ToString(), Brushes.White);
            DrawText(dc, 5, 1, "X", Brushes.Blue);
            for (ushort x = 0; x < 10; x++)
                if (x == 5)
                {
                    Assert.IsTrue(buffer[x, 1].Width == 1);
                    Assert.IsTrue(buffer[x, 1].Foreground.Symbol.Character == 'X');
                    Assert.IsTrue(buffer[x, 1].Foreground.Color == Colors.Blue);
                }
                else
                {
                    Assert.IsTrue(buffer[x, 1].Width == 1);
                    Assert.IsTrue(buffer[x, 1].Foreground.Symbol.Character == x.ToString()[0]);
                }
        }

        [Test]
        public void DrawDoubleWide()
        {
            using var consoleTopLevelImpl = new ConsoleWindowImpl();
            PixelBuffer buffer = consoleTopLevelImpl.PixelBuffer;
            var dc = new DrawingContextImpl(consoleTopLevelImpl);

            for (ushort x = 0; x < 10; x++) DrawText(dc, x, 0, x.ToString(), Brushes.White);
            DrawText(dc, 5, 0, "🏳️‍🌈", Brushes.Blue);
            for (ushort x = 0; x < 10; x++)
            {
                Pixel pixel = buffer[x, 0];
                if (x == 5)
                {
                    Assert.IsTrue(pixel.Width == 2);
                    Assert.IsTrue(pixel.Foreground.Symbol.Character == char.MinValue);
                    Assert.IsTrue(pixel.Foreground.Symbol.Complex == "🏳️‍🌈");
                    Assert.IsTrue(pixel.Foreground.Color == Colors.Blue);
                }
                else if (x == 6)
                {
                    Assert.IsTrue(pixel.Width == 0);
                    Assert.IsTrue(pixel.Foreground.Symbol.Character == 0);
                }
                else
                {
                    Assert.IsTrue(pixel.Width == 1);
                    Assert.IsTrue(pixel.Foreground.Symbol.Character == x.ToString()[0]);
                }
            }
        }

        [Test]
        public void DrawOverDoubleWideFirstChar()
        {
            using var consoleTopLevelImpl = new ConsoleWindowImpl();
            PixelBuffer buffer = consoleTopLevelImpl.PixelBuffer;
            var dc = new DrawingContextImpl(consoleTopLevelImpl);

            for (ushort x = 0; x < 10; x++) DrawText(dc, x, 0, x.ToString(), Brushes.White);
            DrawText(dc, 5, 0, "🏳️‍🌈", Brushes.Blue);
            DrawText(dc, 5, 0, "X", Brushes.Red);
            for (ushort x = 0; x < 10; x++)
            {
                Pixel pixel = buffer[x, 0];
                if (x == 5)
                {
                    Assert.IsTrue(pixel.Width == 1);
                    Assert.IsTrue(pixel.Foreground.Symbol.Character == 'X');
                    Assert.IsTrue(pixel.Foreground.Color == Colors.Red);
                }
                else if (x == 6)
                {
                    Assert.IsTrue(pixel.Width == 0);
                    Assert.IsTrue(pixel.Foreground.Symbol.Character == 0);
                }
                else
                {
                    Assert.IsTrue(pixel.Width == 1);
                    Assert.IsTrue(pixel.Foreground.Symbol.Character == x.ToString()[0]);
                }
            }
        }

        [Test]
        public void DrawOverDoubleWideSecondChar()
        {
            using var consoleTopLevelImpl = new ConsoleWindowImpl();
            PixelBuffer buffer = consoleTopLevelImpl.PixelBuffer;
            var dc = new DrawingContextImpl(consoleTopLevelImpl);

            for (ushort x = 0; x < 10; x++) DrawText(dc, x, 0, x.ToString(), Brushes.White);
            DrawText(dc, 5, 0, "🏳️‍🌈", Brushes.Blue);
            DrawText(dc, 6, 0, "X", Brushes.Red);
            for (ushort x = 0; x < 10; x++)
            {
                Pixel pixel = buffer[x, 0];
                if (x == 5)
                {
                    Assert.IsTrue(pixel.Width == 2,
                        $"[{x},0] Expected wide character with width 2 at position {x}");
                    Assert.IsTrue(pixel.Foreground.Symbol.Complex == "🏳️‍🌈",
                        $"[{x},0] Expected emoji '🏳️‍🌈' to remain at position {x} after drawing 'X' at position 6");
                    Assert.IsTrue(pixel.Foreground.Symbol.Character == char.MinValue,
                        $"[{x},0] Expected Character to be char.MinValue for complex emoji at position {x}");
                    Assert.IsTrue(pixel.Foreground.Color == Colors.Blue,
                        $"[{x},0] Expected foreground color to be Blue at position {x} (wide char was overwritten by next position)");
                }
                else if (x == 6)
                {
                    Assert.IsTrue(pixel.Width == 1,
                        $"[{x},0] Expected single-width character at position {x} after overwriting second cell of wide character");
                    Assert.IsTrue(pixel.Foreground.Symbol.Character == 'X',
                        $"[{x},0] Expected 'X' character at position {x}");
                    Assert.IsNull(pixel.Foreground.Symbol.Complex,
                        $"[{x},0] Expected Complex to be null for simple character 'X' at position {x}");
                    Assert.IsTrue(pixel.Foreground.Color == Colors.Red,
                        $"[{x},0] Expected Red color at position {x} for 'X' character");
                }
                else
                {
                    Assert.IsTrue(pixel.Width == 1,
                        $"[{x},0] Expected single-width character at position {x}");
                    Assert.IsTrue(pixel.Foreground.Symbol.Character == x.ToString()[0],
                        $"[{x},0] Expected digit '{x}' at position {x}");
                    Assert.IsNull(pixel.Foreground.Symbol.Complex,
                        $"[{x},0] Expected Complex to be null for simple digit at position {x}");
                }
            }
        }

        [Test]
        public void DrawTabText()
        {
            using var consoleTopLevelImpl = new ConsoleWindowImpl();
            PixelBuffer buffer = consoleTopLevelImpl.PixelBuffer;
            var dc = new DrawingContextImpl(consoleTopLevelImpl);
            DrawText(dc, 0, 0, "A\tB", Brushes.White);
            Assert.IsTrue(buffer[0, 0].Foreground.Symbol.Character == 'A');
            for (ushort i = 1; i < 5; i++)
                Assert.IsTrue(buffer[i, 0].Foreground.Symbol.Character == ' ');
            Assert.IsTrue(buffer[5, 0].Foreground.Symbol.Character == 'B');
        }

        [Test]
        public void DrawTabTextClippedStart()
        {
            using var consoleTopLevelImpl = new ConsoleWindowImpl();
            PixelBuffer buffer = consoleTopLevelImpl.PixelBuffer;
            var dc = new DrawingContextImpl(consoleTopLevelImpl);
            DrawText(dc, 0, 0, "xxxxxxxxxx", Brushes.White);

            for (ushort x = 0; x < 10; x++)
                Assert.IsTrue(buffer[x, 0].Foreground.Symbol.Character == 'x');

            dc.PushClip(new Rect(2, 0, 10, 1));
            DrawText(dc, 0, 0, "A\tB", Brushes.White);
            Assert.IsTrue(buffer[0, 0].Foreground.Symbol.Character == 'x');
            Assert.IsTrue(buffer[1, 0].Foreground.Symbol.Character == 'x');
            for (ushort i = 2; i < 5; i++)
                Assert.IsTrue(buffer[i, 0].Foreground.Symbol.Character == ' ');
            Assert.IsTrue(buffer[5, 0].Foreground.Symbol.Character == 'B');
            Assert.IsTrue(buffer[6, 0].Foreground.Symbol.Character == 'x');
        }

        [Test]
        public void DrawTabTextClippedEnd()
        {
            using var consoleTopLevelImpl = new ConsoleWindowImpl();
            PixelBuffer buffer = consoleTopLevelImpl.PixelBuffer;
            var dc = new DrawingContextImpl(consoleTopLevelImpl);
            DrawText(dc, 0, 0, "xxxxxxxxxx", Brushes.White);

            for (ushort x = 0; x < 10; x++)
                Assert.IsTrue(buffer[x, 0].Foreground.Symbol.Character == 'x');

            dc.PushClip(new Rect(0, 0, 3, 1));
            DrawText(dc, 0, 0, "A\tB", Brushes.White);
            Assert.IsTrue(buffer[0, 0].Foreground.Symbol.Character == 'A');
            Assert.IsTrue(buffer[1, 0].Foreground.Symbol.Character == ' ');
            Assert.IsTrue(buffer[2, 0].Foreground.Symbol.Character == ' ');
            for (ushort i = 3; i < 10; i++)
                Assert.IsTrue(buffer[i, 0].Foreground.Symbol.Character == 'x');
        }


        [Test]
        public void DrawLineStrikethrough()
        {
            using var consoleTopLevelImpl = new ConsoleWindowImpl();
            PixelBuffer buffer = consoleTopLevelImpl.PixelBuffer;
            var dc = new DrawingContextImpl(consoleTopLevelImpl);

            DrawText(dc, 1, 0, "hello", Brushes.Blue);
            SetOrigin(dc, 1, 0);
            dc.DrawLine(new Pen(Brushes.White, DrawingContextImpl.StrikethroughThickness), new Point(0, 0),
                new Point(6, 0));
            Assert.IsTrue(buffer[0, 0].Foreground.TextDecoration == null);
            Assert.IsTrue(buffer[1, 0].Foreground.TextDecoration == TextDecorationLocation.Strikethrough);
            Assert.IsTrue(buffer[2, 0].Foreground.TextDecoration == TextDecorationLocation.Strikethrough);
            Assert.IsTrue(buffer[3, 0].Foreground.TextDecoration == TextDecorationLocation.Strikethrough);
            Assert.IsTrue(buffer[4, 0].Foreground.TextDecoration == TextDecorationLocation.Strikethrough);
            Assert.IsTrue(buffer[5, 0].Foreground.TextDecoration == TextDecorationLocation.Strikethrough);
            Assert.IsTrue(buffer[6, 0].Foreground.TextDecoration == null);
        }

        [Test]
        public void DrawLineUnderline()
        {
            using var consoleTopLevelImpl = new ConsoleWindowImpl();
            PixelBuffer buffer = consoleTopLevelImpl.PixelBuffer;
            var dc = new DrawingContextImpl(consoleTopLevelImpl);

            DrawText(dc, 1, 1, "hello", Brushes.Blue);
            SetOrigin(dc, 1, 1);
            dc.DrawLine(new Pen(Brushes.White, DrawingContextImpl.UnderlineThickness), new Point(0, 0),
                new Point(6, 0));
            Assert.IsTrue(buffer[0, 1].Foreground.TextDecoration == null);
            Assert.IsTrue(buffer[1, 1].Foreground.TextDecoration == TextDecorationLocation.Underline);
            Assert.IsTrue(buffer[2, 1].Foreground.TextDecoration == TextDecorationLocation.Underline);
            Assert.IsTrue(buffer[3, 1].Foreground.TextDecoration == TextDecorationLocation.Underline);
            Assert.IsTrue(buffer[4, 1].Foreground.TextDecoration == TextDecorationLocation.Underline);
            Assert.IsTrue(buffer[5, 1].Foreground.TextDecoration == TextDecorationLocation.Underline);
            Assert.IsTrue(buffer[6, 1].Foreground.TextDecoration == null);
        }


        [Test]
        public void DrawHorizontalLine()
        {
            using var consoleTopLevelImpl = new ConsoleWindowImpl();
            PixelBuffer buffer = consoleTopLevelImpl.PixelBuffer;
            var dc = new DrawingContextImpl(consoleTopLevelImpl);
            SetOrigin(dc, 1, 1);
            dc.DrawLine(new Pen(Brushes.White), new Point(0, 0), new Point(4, 0));
            for (ushort x = 0; x <= 6; x++)
                if (x == 0 || x == 6)
                {
                    Assert.IsTrue(buffer[x, 1].Foreground.Symbol.Character == ' ');
                    Assert.IsNull(buffer[x, 1].Foreground.Symbol.Complex);
                    Assert.IsTrue(buffer[x, 1].Foreground.Color == Colors.Transparent);
                }
                else
                {
                    Assert.IsTrue(buffer[x, 1].Foreground.Symbol.Character == '─');
                    Assert.IsNull(buffer[x, 1].Foreground.Symbol.Complex);
                    Assert.IsTrue(buffer[x, 1].Foreground.Color == Colors.White);
                }
        }

        [Test]
        public void DrawVerticalLine()
        {
            using var consoleTopLevelImpl = new ConsoleWindowImpl();
            PixelBuffer buffer = consoleTopLevelImpl.PixelBuffer;
            var dc = new DrawingContextImpl(consoleTopLevelImpl);
            SetOrigin(dc, 1, 1);

            dc.DrawLine(new Pen(Brushes.White), new Point(0, 0), new Point(0, 4));
            for (ushort y = 0; y <= 6; y++)
                if (y == 0 || y == 6)
                {
                    Assert.IsTrue(buffer[1, y].Foreground.Symbol.Character == ' ');
                    Assert.IsNull(buffer[1, y].Foreground.Symbol.Complex);
                    Assert.IsTrue(buffer[1, y].Foreground.Color == Colors.Transparent);
                }
                else
                {
                    Assert.IsTrue(buffer[1, y].Foreground.Symbol.Character == '│');
                    Assert.IsNull(buffer[1, y].Foreground.Symbol.Complex);
                    Assert.IsTrue(buffer[1, y].Foreground.Color == Colors.White);
                }
        }

        [Test]
        public void DrawLinesCrossingMakeCross()
        {
            using var consoleTopLevelImpl = new ConsoleWindowImpl();
            PixelBuffer buffer = consoleTopLevelImpl.PixelBuffer;
            var dc = new DrawingContextImpl(consoleTopLevelImpl);

            SetOrigin(dc, 1, 1);
            for (int y = 0; y < 5; y += 2)
                dc.DrawLine(new Pen(Brushes.White), new Point(0, y), new Point(4, y));

            for (int x = 0; x < 5; x += 2)
                dc.DrawLine(new Pen(Brushes.White), new Point(x, 0), new Point(x, 4));

            // line 1
            Assert.IsTrue(buffer[1, 1].Foreground.Symbol.Character == '┌');
            Assert.IsTrue(buffer[2, 1].Foreground.Symbol.Character == '─');
            Assert.IsTrue(buffer[3, 1].Foreground.Symbol.Character == '┬');
            Assert.IsTrue(buffer[4, 1].Foreground.Symbol.Character == '─');
            Assert.IsTrue(buffer[5, 1].Foreground.Symbol.Character == '┐');

            // line 2 
            Assert.IsTrue(buffer[1, 2].Foreground.Symbol.Character == '│');
            Assert.IsTrue(buffer[2, 2].Foreground.Symbol.Character == ' ');
            Assert.IsTrue(buffer[3, 2].Foreground.Symbol.Character == '│');
            Assert.IsTrue(buffer[4, 2].Foreground.Symbol.Character == ' ');
            Assert.IsTrue(buffer[5, 2].Foreground.Symbol.Character == '│');

            // line 3
            Assert.IsTrue(buffer[1, 3].Foreground.Symbol.Character == '├');
            Assert.IsTrue(buffer[2, 3].Foreground.Symbol.Character == '─');
            Assert.IsTrue(buffer[3, 3].Foreground.Symbol.Character == '┼');
            Assert.IsTrue(buffer[4, 3].Foreground.Symbol.Character == '─');
            Assert.IsTrue(buffer[5, 3].Foreground.Symbol.Character == '┤');


            // line 4
            Assert.IsTrue(buffer[1, 4].Foreground.Symbol.Character == '│');
            Assert.IsTrue(buffer[2, 4].Foreground.Symbol.Character == ' ');
            Assert.IsTrue(buffer[3, 4].Foreground.Symbol.Character == '│');
            Assert.IsTrue(buffer[4, 4].Foreground.Symbol.Character == ' ');
            Assert.IsTrue(buffer[5, 4].Foreground.Symbol.Character == '│');

            // line 5
            Assert.IsTrue(buffer[1, 5].Foreground.Symbol.Character == '└');
            Assert.IsTrue(buffer[2, 5].Foreground.Symbol.Character == '─');
            Assert.IsTrue(buffer[3, 5].Foreground.Symbol.Character == '┴');
            Assert.IsTrue(buffer[4, 5].Foreground.Symbol.Character == '─');
            Assert.IsTrue(buffer[5, 5].Foreground.Symbol.Character == '┘');
        }

        [Test]
        public void DrawSolidRectangle()
        {
            using var consoleTopLevelImpl = new ConsoleWindowImpl();
            PixelBuffer buffer = consoleTopLevelImpl.PixelBuffer;
            var dc = new DrawingContextImpl(consoleTopLevelImpl);
            SetOrigin(dc, 1, 1);
            // NOTE: Rect in avalonia has interesting semantics
            // Left and Top are INCLUSIVE
            // Right and Bottom are EXCLUSIVE.
            // So width=3 and height=3 means pixels at 0,1,2 even though Right = 4 and Bottom = 4
            // Bottom and Right cells should NOT be drawn into.
            int width = 3;
            int height = 3;
            int left = 1;
            int top = 1;
            int right = left + width;
            int bottom = top + height;
            dc.DrawRectangle(Brushes.Blue, null, new Rect(0, 0, width, height));

            for (ushort y = 0; y <= bottom; y++)
            for (ushort x = 0; x <= right; x++)
                if (x == 0)
                {
                    Assert.IsTrue(buffer[x, y].Foreground.Symbol.Character == ' ',
                        $"[{x},{y}] Expected empty char outside left border");
                    Assert.IsTrue(buffer[x, y].Foreground.Color == Colors.Transparent,
                        $"[{x},{y}] Expected transparent foreground color outside left border");
                    Assert.IsTrue(buffer[x, y].Background.Color == Colors.Transparent,
                        $"[{x},{y}] Expected transparent background color outside left border");
                }
                else if (y == 0)
                {
                    Assert.IsTrue(buffer[x, y].Foreground.Symbol.Character == ' ',
                        $"[{x},{y}] Expected empty char outside top border");
                    Assert.IsTrue(buffer[x, y].Foreground.Color == Colors.Transparent,
                        $"[{x},{y}] Expected transparent foreground color outside top border");
                    Assert.IsTrue(buffer[x, y].Background.Color == Colors.Transparent,
                        $"[{x},{y}] Expected transparent background color outside top border");
                }
                else if (x >= right)
                {
                    Assert.IsTrue(buffer[x, y].Foreground.Symbol.Character == ' ',
                        $"[{x},{y}] Expected empty char outside right border");
                    Assert.IsTrue(buffer[x, y].Foreground.Color == Colors.Transparent,
                        $"[{x},{y}] Expected transparent foreground  color outside right border");
                    Assert.IsTrue(buffer[x, y].Background.Color == Colors.Transparent,
                        $"[{x},{y}] Expected transparent background color outside right border");
                }
                else if (y >= bottom)
                {
                    Assert.IsTrue(buffer[x, y].Foreground.Symbol.Character == ' ',
                        $"[{x},{y}] Expected empty char outside bottom border");
                    Assert.IsTrue(buffer[x, y].Foreground.Color == Colors.Transparent,
                        $"[{x},{y}] Expected transparent foreground color outside bottom border");
                    Assert.IsTrue(buffer[x, y].Background.Color == Colors.Transparent,
                        $"[{x},{y}] Expected transparent background color outside bottom border");
                }
                else
                {
                    Assert.IsTrue(buffer[x, y].Foreground.Symbol.Character == ' ',
                        $"[{x},{y}]  Expected empty char inside rectangle");
                    Assert.IsTrue(buffer[x, y].Foreground.Color == Colors.Transparent,
                        $"[{x},{y}] Expected transparent foreground color inside rectangle");
                    Assert.IsTrue(buffer[x, y].Background.Color == Colors.Blue,
                        $"[{x},{y}] Expected blue background color inside rectangle");
                }
        }

        private static readonly char[] SingleBoxChars =
        {
            '┌', '─', '┐',
            '│', '│',
            '└', '─', '┘'
        };

        private static readonly char[] DoubleBoxChars =
        {
            '╔', '═', '╗',
            '║', '║',
            '╚', '═', '╝'
        };

        private static readonly char[] EdgeBoxChars =
        {
            ' ', '▁', ' ',
            '▕', '▏',
            ' ', '▔', ' '
        };

        private static readonly char[] EdgeWideBoxChars =
        {
            '▗', '▄', '▖',
            '▐', '▌',
            '▝', '▀', '▘'
        };

        private static readonly char[] BoldBoxChars =
        {
            '█', '█', '█',
            '█', '█',
            '█', '█', '█'
        };

        private static readonly char[] EmptyBoxChars =
        {
            ' ', ' ', ' ',
            ' ', ' ',
            ' ', ' ', ' '
        };

        private enum LinePositions
        {
            UpperLeft = 0,
            Top = 1,
            UpperRight = 2,
            Left = 3,
            Right = 4,
            LowerLeft = 5,
            Bottom = 6,
            LowerRight = 7
        }

        private static readonly object[] BoxVariations =
        {
            new object[] { null, EmptyBoxChars },
            new object[] { new Pen(Brushes.Red), SingleBoxChars },
            new object[]
                { new Pen(new LineBrush { Brush = Brushes.Red, LineStyle = LineStyle.SingleLine }), SingleBoxChars },
            new object[]
                { new Pen(new LineBrush { Brush = Brushes.Red, LineStyle = LineStyle.DoubleLine }), DoubleBoxChars },
            new object[] { new Pen(new LineBrush { Brush = Brushes.Red, LineStyle = LineStyle.Edge }), EdgeBoxChars },
            new object[]
                { new Pen(new LineBrush { Brush = Brushes.Red, LineStyle = LineStyle.EdgeWide }), EdgeWideBoxChars },
            new object[] { new Pen(new LineBrush { Brush = Brushes.Red, LineStyle = LineStyle.Bold }), BoldBoxChars }
        };

        // NOTE: <Rectangle with stroke in avalonia has interesting semantics
        // <Rectangle with a Pen ends up sending us a rectangle which is 1 smaller on width and height, and .5 as origin so that strokes
        // overlap the edge of the rectangle.
        // We have a bunch of scenarios to test
        // * No pen - just fill the rectangle
        // * Edge chars are drawn outside of the rectangle
        // * Line chars are drawn inside of the rectangle
        // * Rectangle width/height is adjusted by 1 in both dimensions when there is a pen.
        /* Example markup for these test cases.
    <Window.Resources>
        <console:LineBrush x:Key="EdgeBlack" LineStyle="Edge" Brush="Black"/>
        <console:LineBrush x:Key="EdgeWideBlack" LineStyle="EdgeWide" Brush="Black"/>
    </Window.Resources>

    <StackPanel Orientation="Vertical" Spacing="2" Margin="2">
        <!-- just rectangles -->
        <TextBlock>3X3 Rectangle with brush and pen</TextBlock>
        <StackPanel Orientation="Horizontal" Spacing="2">
            <TextBlock>Single</TextBlock>
            <Rectangle Fill="Pink" Width="3" Height="3" Grid.Column="0" Grid.Row="0" StrokeThickness="1" Stroke="Black" />
            <TextBlock>Edge</TextBlock>
            <Rectangle Fill="Pink" Width="3" Height="3" Grid.Column="0" Grid.Row="1" StrokeThickness="1" Stroke="{StaticResource EdgeBlack}"/>
            <TextBlock>EdgeWide</TextBlock>
            <Rectangle Fill="Pink" Width="3" Height="3" Grid.Column="0" Grid.Row="2" StrokeThickness="1" Stroke="{StaticResource EdgeWideBlack}"/>
        </StackPanel>

        <!-- just border wih no background -->
        <TextBlock>Border with pen around 3X3 rectangle with brush</TextBlock>
        <StackPanel Orientation="Horizontal" Spacing="2">
            <TextBlock>Single</TextBlock>
            <Border BorderBrush="Black"
                    BorderThickness="1">
                <Rectangle Fill="Pink" Width="3" Height="3" />
            </Border>

            <TextBlock>Edge</TextBlock>
            <Border BorderThickness="1"
                    BorderBrush="{StaticResource EdgeBlack}" >
                <Rectangle Fill="Pink" Width="3" Height="3" />
            </Border>

            <TextBlock>EdgeWide</TextBlock>
            <Border BorderThickness="1"
                    BorderBrush="{StaticResource EdgeWideBlack}" >
                <Rectangle Fill="Pink" Width="3" Height="3" />
            </Border>
        </StackPanel>

        <!-- border background -->
        <TextBlock>3X3 Border with brush and pen</TextBlock>
        <StackPanel Orientation="Horizontal" Spacing="2">
            <TextBlock>Single</TextBlock>
            <Border BorderBrush="Black"
                    BorderThickness="1"
                    Background="Pink"
                    Width="3" Height="3"/>
            <TextBlock>Edge</TextBlock>
            <Border BorderBrush="{StaticResource EdgeBlack}"
                    BorderThickness="1"
                    Background="Pink"
                    Width="3" Height="3"/>
            <TextBlock>EdgeWide</TextBlock>
            <Border BorderBrush="{StaticResource EdgeWideBlack}"
                    BorderThickness="1"
                    Background="Pink"
                    Width="3" Height="3"/>
        </StackPanel>
    </StackPanel>
        */
        [TestCaseSource(nameof(BoxVariations))]
        public void DrawRectangleWithPen(IPen pen, char[] boxChars)
        {
            using var
                consoleTopLevelImpl =
                    new ConsoleWindowImpl(); //todo: low: this and other initializations can be moved to test initialization
            PixelBuffer buffer = consoleTopLevelImpl.PixelBuffer;
            var dc = new DrawingContextImpl(consoleTopLevelImpl);
            SetOrigin(dc, 1, 1);
            // NOTE: Avalonia <Rectangle> ends up sending us a rectangle which is 1 smaller on width and height, this is testing that code path.
            Rect rect = pen != null
                ? new Rect(.5, .5, 1, 1)
                : // pen has smaller rect
                new Rect(.5, .5, 2, 2); // no pen has original rect
            dc.DrawRectangle(Brushes.Blue, pen, new RoundedRect(rect));
            bool isOuterBox = pen?.Brush is LineBrush lineBrush && lineBrush.HasEdgeLineStyle();

            // move to origin location
            rect = pen != null
                ?
                // pen needs to expand the width and height of where we think the rectangle is
                new Rect(rect.Left + 1, rect.Top + 1, rect.Width + 1, rect.Height + 1)
                :
                // no pen just needs to move to the origin location
                new Rect(rect.Left + 1, rect.Top + 1, rect.Width, rect.Height);

            var newRect = rect.ToPixelRect();
            int bottomRow = newRect.Bottom - 1;
            int rightCol = newRect.Right - 1;
            for (ushort y = 0; y <= newRect.Bottom; y++)
            for (ushort x = 0; x <= newRect.Right; x++)
                if (x < newRect.X || x >= newRect.Right ||
                    y < newRect.Y || y >= newRect.Bottom)
                {
                    // outside of box
                    Assert.IsTrue(buffer[x, y].Foreground.Symbol.Character == ' ',
                        $"[{x},{y}] Outside of box expected empty char");
                    Assert.IsTrue(buffer[x, y].Foreground.Color == Colors.Transparent,
                        $"[{x},{y}] outside of box expected transparent Foreground");
                    Assert.IsTrue(buffer[x, y].Background.Color == Colors.Transparent,
                        $"[{x},{y}] Outside of box expected transparent background");
                }
                else if (x == newRect.X && y == newRect.Y)
                {
                    // upper left corner
                    Assert.IsTrue(buffer[x, y].Foreground.Symbol.Character == boxChars[(int)LinePositions.UpperLeft],
                        $"[{x},{y}] [{x},{y}] Upper left corner expected {boxChars[(int)LinePositions.UpperLeft]}");
                    if (boxChars[(int)LinePositions.UpperLeft] == ' ')
                        Assert.IsTrue(buffer[x, y].Foreground.Color == Colors.Transparent,
                            $"[{x},{y}] Upper left corner expected transparent for empty char");
                    else
                        Assert.IsTrue(buffer[x, y].Foreground.Color == Colors.Red,
                            $"[{x},{y}] Upper left corner expected foreground of red for non empty char");

                    if (isOuterBox)
                        Assert.IsTrue(buffer[x, y].Background.Color == Colors.Transparent,
                            $"[{x},{y}] Upper left corner of outer box expected transparent background");
                    else
                        Assert.IsTrue(buffer[x, y].Background.Color == Colors.Blue,
                            $"[{x},{y}] Upper left corner expected blue background");
                }
                else if (x == rightCol && y == newRect.Y)
                {
                    // upper right corner
                    Assert.IsTrue(buffer[x, y].Foreground.Symbol.Character == boxChars[(int)LinePositions.UpperRight],
                        $"[{x},{y}] Upper right corner expected {boxChars[(int)LinePositions.UpperRight]}");
                    if (boxChars[(int)LinePositions.UpperRight] == ' ')
                        Assert.IsTrue(buffer[x, y].Foreground.Color == Colors.Transparent,
                            $"[{x},{y}] Upper right corner expected transparent for empty char");
                    else
                        Assert.IsTrue(buffer[x, y].Foreground.Color == Colors.Red,
                            $"[{x},{y}] Upper right corner expected foreground of red for non empty char");

                    if (isOuterBox)
                        Assert.IsTrue(buffer[x, y].Background.Color == Colors.Transparent,
                            $"[{x},{y}] Upper left corner of outer box expected transparent background");
                    else
                        Assert.IsTrue(buffer[x, y].Background.Color == Colors.Blue,
                            $"[{x},{y}] Upper right corner expected blue background");
                }
                else if (x == rightCol && y == bottomRow)
                {
                    // lower right corner
                    Assert.IsTrue(buffer[x, y].Foreground.Symbol.Character == boxChars[(int)LinePositions.LowerRight],
                        $"[{x},{y}] Lower right corner expected {boxChars[(int)LinePositions.LowerRight]}");
                    if (boxChars[(int)LinePositions.LowerRight] == ' ')
                        Assert.IsTrue(buffer[x, y].Foreground.Color == Colors.Transparent,
                            $"[{x},{y}] Lower right corner expected transparent for empty char");
                    else
                        Assert.IsTrue(buffer[x, y].Foreground.Color == Colors.Red,
                            $"[{x},{y}] Lower right corner expected foreground of red for non empty char");
                    if (isOuterBox)
                        Assert.IsTrue(buffer[x, y].Background.Color == Colors.Transparent,
                            $"[{x},{y}] Upper left corner of outer box expected transparent background");
                    else
                        Assert.IsTrue(buffer[x, y].Background.Color == Colors.Blue,
                            $"[{x},{y}] Lower right corner expected blue background");
                }
                else if (x == newRect.X && y == bottomRow)
                {
                    // lower left corner
                    Assert.IsTrue(buffer[x, y].Foreground.Symbol.Character == boxChars[(int)LinePositions.LowerLeft],
                        $"[{x},{y}] Lower left corner expected {boxChars[(int)LinePositions.LowerLeft]}");
                    if (boxChars[(int)LinePositions.LowerLeft] == ' ')
                        Assert.IsTrue(buffer[x, y].Foreground.Color == Colors.Transparent,
                            $"[{x},{y}] Lower left corner expected transparent for empty char");
                    else
                        Assert.IsTrue(buffer[x, y].Foreground.Color == Colors.Red,
                            $"[{x},{y}] Lower left corner expected foreground of red for non empty char");
                    if (isOuterBox)
                        Assert.IsTrue(buffer[x, y].Background.Color == Colors.Transparent,
                            $"[{x},{y}] Upper left corner of outer box expected transparent background");
                    else
                        Assert.IsTrue(buffer[x, y].Background.Color == Colors.Blue,
                            $"[{x},{y}] Lower left corner expected blue background");
                }
                else if (x == newRect.X && y >= newRect.Y && y < newRect.Bottom)
                {
                    // left side
                    Assert.IsTrue(buffer[x, y].Foreground.Symbol.Character == boxChars[(int)LinePositions.Left],
                        $"[{x},{y}] Left side expected {boxChars[(int)LinePositions.Left]}");
                    if (boxChars[(int)LinePositions.Left] == ' ')
                        Assert.IsTrue(buffer[x, y].Foreground.Color == Colors.Transparent,
                            $"[{x},{y}] Left side expected transparent for empty char");
                    else
                        Assert.IsTrue(buffer[x, y].Foreground.Color == Colors.Red,
                            $"[{x},{y}] Left side expected foreground of red for non empty char");
                    if (isOuterBox)
                        Assert.IsTrue(buffer[x, y].Background.Color == Colors.Transparent,
                            $"[{x},{y}] Upper left corner of outer box expected transparent background");
                    else
                        Assert.IsTrue(buffer[x, y].Background.Color == Colors.Blue,
                            $"[{x},{y}] Left side expected blue background");
                }
                else if (y == newRect.Y && x >= newRect.X && x < rightCol)
                {
                    //top side
                    Assert.IsTrue(buffer[x, y].Foreground.Symbol.Character == boxChars[(int)LinePositions.Top],
                        $"[{x},{y}] Top side expected {boxChars[(int)LinePositions.Top]}");
                    if (boxChars[(int)LinePositions.Top] == ' ')
                        Assert.IsTrue(buffer[x, y].Foreground.Color == Colors.Transparent,
                            $"[{x},{y}] Top side expected transparent for empty char");
                    else
                        Assert.IsTrue(buffer[x, y].Foreground.Color == Colors.Red,
                            $"[{x},{y}] Top side expected foreground of red for non empty char");
                    if (isOuterBox)
                        Assert.IsTrue(buffer[x, y].Background.Color == Colors.Transparent,
                            $"[{x},{y}] Upper left corner of outer box expected transparent background");
                    else
                        Assert.IsTrue(buffer[x, y].Background.Color == Colors.Blue,
                            $"[{x},{y}] Top side expected blue background");
                }
                else if (x == rightCol && y >= newRect.Y && y < newRect.Bottom)
                {
                    // right side
                    Assert.IsTrue(buffer[x, y].Foreground.Symbol.Character == boxChars[(int)LinePositions.Right],
                        $"[{x},{y}] Right side expected {boxChars[(int)LinePositions.Right]}");
                    if (boxChars[(int)LinePositions.Right] == ' ')
                        Assert.IsTrue(buffer[x, y].Foreground.Color == Colors.Transparent,
                            $"[{x},{y}] Right side expected transparent for empty char");
                    else
                        Assert.IsTrue(buffer[x, y].Foreground.Color == Colors.Red,
                            $"[{x},{y}] Right side expected foreground of red for non empty char");
                    if (isOuterBox)
                        Assert.IsTrue(buffer[x, y].Background.Color == Colors.Transparent,
                            $"[{x},{y}] Upper left corner of outer box expected transparent background");
                    else
                        Assert.IsTrue(buffer[x, y].Background.Color == Colors.Blue,
                            $"[{x},{y}] Right side expected blue background");
                }
                else if (y == bottomRow && x >= newRect.X && x < newRect.Right)
                {
                    // bottom side
                    Assert.IsTrue(buffer[x, y].Foreground.Symbol.Character == boxChars[(int)LinePositions.Bottom],
                        $"[{x},{y}] Bottom side expected {boxChars[(int)LinePositions.Bottom]}");
                    if (boxChars[(int)LinePositions.Bottom] == ' ')
                        Assert.IsTrue(buffer[x, y].Foreground.Color == Colors.Transparent,
                            $"[{x},{y}] Bottom side expected transparent for empty char");
                    else
                        Assert.IsTrue(buffer[x, y].Foreground.Color == Colors.Red,
                            $"[{x},{y}] Bottom side expected foreground of red for non empty char");
                    if (isOuterBox)
                        Assert.IsTrue(buffer[x, y].Background.Color == Colors.Transparent,
                            $"[{x},{y}] Upper left corner of outer box expected transparent background");
                    else
                        Assert.IsTrue(buffer[x, y].Background.Color == Colors.Blue,
                            $"[{x},{y}] Bottom side expected blue background");
                }
                else if (x > newRect.X && x < rightCol &&
                         y > newRect.Y && y < bottomRow)
                {
                    // inside
                    Assert.IsTrue(buffer[x, y].Foreground.Symbol.Character == ' ', $"[{x},{y}] Inside expected ' '");
                    Assert.IsTrue(buffer[x, y].Foreground.Color == Colors.Transparent,
                        $"[{x},{y}] Inside expected transparent for empty char");
                    Assert.IsTrue(buffer[x, y].Background.Color == Colors.Blue,
                        $"[{x},{y}] Inside expected blue background");
                }
        }

        internal static void DrawText(DrawingContextImpl dc, ushort x, ushort y, string text, IBrush brush)
        {
            SetOrigin(dc, x, y);
            var platformRender = AvaloniaLocator.Current.GetService<IPlatformRenderInterface>();
            var textShaper = AvaloniaLocator.Current.GetService<ITextShaperImpl>();
            var fontManager = AvaloniaLocator.Current.GetService<IFontManagerImpl>();
            fontManager.TryCreateGlyphTypeface(FontManager.Current.DefaultFontFamily.Name, FontStyle.Normal,
                FontWeight.Normal,
                FontStretch.Normal,
                out IGlyphTypeface typeface);
            ArgumentNullException.ThrowIfNull(typeface);
            ShapedBuffer glyphs =
                textShaper.ShapeText(text.AsMemory(), new TextShaperOptions(typeface, typeface.Metrics.DesignEmHeight));
            //var shapedText = new GlyphRun(typeface,
            //    1,
            //    text.AsMemory(),
            //    glyphs,
            //    default(Point),
            //    0);
            IGlyphRunImpl glyphRunImpl = platformRender.CreateGlyphRun(typeface, 1, glyphs, default);
            dc.DrawGlyphRun(brush, glyphRunImpl);
        }

        private static readonly object[] OverlapBoxVariations =
        {
            new object[]
            {
                new Rect(-.5, 0, 2, 2),
                new Pen(new LineBrush { Brush = Brushes.Black, LineStyle = LineStyle.SingleLine }),
                """
                ─┐                                                                              
                 │                                                                              
                ─┘
                """
            },

            new object[]
            {
                new Rect(0.5, 0.5, 1, 1),
                new Pen(new LineBrush { Brush = Brushes.Black, LineStyle = LineStyle.SingleLine }),
                """
                ┌┐                                                                              
                └┘
                """
            },
            new object[]
            {
                new Rect(-1.5, 0, 2, 2),
                new Pen(new LineBrush { Brush = Brushes.Black, LineStyle = LineStyle.SingleLine }),
                """
                ┐                                                                               
                │                                                                               
                ┘
                """
            },
            new object[]
            {
                new Rect(-.5, 0, 2, 2),
                new Pen(new LineBrush { Brush = Brushes.Black, LineStyle = LineStyle.DoubleLine }),
                """
                ═╗                                                                              
                 ║                                                                              
                ═╝
                """
            },
            new object[]
            {
                new Rect(-.5, 0, 2, 2),
                new Pen(new LineBrush { Brush = Brushes.Black, LineStyle = LineStyle.Edge }),
                """
                ▁                                                                               
                 ▏                                                                              
                ▔                              
                """
            },
            new object[]
            {
                new Rect(-1.5, 0, 2, 2),
                new Pen(new LineBrush { Brush = Brushes.Black, LineStyle = LineStyle.Edge }),
                """

                ▏                                                                               

                """
            },
            new object[]
            {
                new Rect(-.5, 0, 2, 2),
                new Pen(new LineBrush { Brush = Brushes.Black, LineStyle = LineStyle.EdgeWide }),
                """
                ▄▖                                                                              
                 ▌                                                                              
                ▀▘                                                                              
                """
            },
            new object[]
            {
                new Rect(0, -.5, 2, 2),
                new Pen(new LineBrush { Brush = Brushes.Black, LineStyle = LineStyle.SingleLine }),
                """
                │ │                                                                             
                └─┘                                                                             
                """
            },
            new object[]
            {
                new Rect(0, -1.5, 2, 2),
                new Pen(new LineBrush { Brush = Brushes.Black, LineStyle = LineStyle.SingleLine }),
                """
                └─┘                                                                             
                """
            },
            new object[]
            {
                new Rect(0, -.5, 2, 2),
                new Pen(new LineBrush { Brush = Brushes.Black, LineStyle = LineStyle.DoubleLine }),
                """
                ║ ║                                                                             
                ╚═╝                                                                             
                """
            },
            new object[]
            {
                new Rect(0, -.5, 2, 2),
                new Pen(new LineBrush { Brush = Brushes.Black, LineStyle = LineStyle.Edge }),
                """
                ▕ ▏                                                                             
                 ▔                                                                              
                """
            },
            new object[]
            {
                new Rect(0, -1.5, 2, 2),
                new Pen(new LineBrush { Brush = Brushes.Black, LineStyle = LineStyle.Edge }),
                """
                 ▔                                                                              
                """
            },
            new object[]
            {
                new Rect(0, -.5, 2, 2),
                new Pen(new LineBrush { Brush = Brushes.Black, LineStyle = LineStyle.EdgeWide }),
                """
                ▐ ▌                                                                             
                ▝▀▘                                                                             
                """
            }
        };


        [TestCaseSource(nameof(OverlapBoxVariations))]
        public void DrawBoxOverEdge(Rect rect, IPen pen, string expected)
        {
            using var consoleTopLevelImpl = new ConsoleWindowImpl();
            PixelBuffer buffer = consoleTopLevelImpl.PixelBuffer;
            var dc = new DrawingContextImpl(consoleTopLevelImpl);
            dc.DrawRectangle(null, pen, rect);

            string text = buffer.PrintBuffer();
            Assert.AreEqual(expected.Trim(), text.Trim());
        }

        private static readonly object[] LineVariations =
        {
            new object[]
            {
                new Point(0.5, 0.5),
                new Point(1.5, 0.5),
                """
                ──                                                                              
                """
            },
            new object[]
            {
                new Point(-0.5, 0.5),
                new Point(0.5, 0.5),
                """
                ─                
                """
            },
            new object[]
            {
                new Point(0.5, 0.5),
                new Point(0.5, 1.5),
                """
                │                                                                               
                │  
                """
            },
            new object[]
            {
                new Point(0.5, -0.5),
                new Point(0.5, 0.5),
                """
                │                                                                               
                """
            }
        };

        [TestCaseSource(nameof(LineVariations))]
        public void DrawLines(Point start, Point end, string expected)
        {
            using var consoleTopLevelImpl = new ConsoleWindowImpl();
            PixelBuffer buffer = consoleTopLevelImpl.PixelBuffer;
            var dc = new DrawingContextImpl(consoleTopLevelImpl);
            dc.DrawLine(new Pen(Brushes.Black), start, end);
            string text = buffer.PrintBuffer();
            Assert.AreEqual(expected.Trim(), text.Trim());
        }

        internal static PixelBufferCoordinate SetOrigin(DrawingContextImpl dc, ushort x, ushort y)
        {
            dc.Transform = new Matrix(1, 0, 0, 1, x, y);
            return new PixelBufferCoordinate(x, y);
        }
    }
}