using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.Platform;
using Avalonia.Platform.Surfaces;
using BenchmarkDotNet.Attributes;
using Consolonia.Controls;
using Consolonia.Core.Drawing;
using Consolonia.Core.Drawing.PixelBufferImplementation;
using Consolonia.Core.Infrastructure;
using Consolonia.Fonts;
using Consolonia.NUnit;

namespace Consolonia.Benchmarks
{
    /// <summary>
    ///     One whole frame of a screen of text, no pictures: a background filled, a border drawn and a
    ///     line of text per row, through the drawing context into the pixel buffer, then rendered to
    ///     the console.
    /// </summary>
    /// <remarks>
    ///     The console is the unit test console, which keeps no output, so what is measured is the work
    ///     Consolonia does per frame for an ordinary text UI: the blends into the pixel buffer and the
    ///     render target's compare of every cell against its cache. The glyph runs are shaped once; the
    ///     scenarios are the screen drawn again unchanged (as when anything repaints), one character
    ///     typed, and a screen whose every cell changed (a scroll). The results are in rendering.md.
    /// </remarks>
    [MemoryDiagnoser]
    [SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable",
        Justification = "BenchmarkDotNet owns the instance and calls the cleanup")]
    public class TextFrameBenchmarks
    {
        private const int CellsWide = CellImageFrameBenchmarks.CellsWide;
        private const int CellsHigh = CellImageFrameBenchmarks.CellsHigh;
        private static readonly IBrush Background = new SolidColorBrush(Color.FromRgb(20, 30, 40));
        private static readonly IBrush Foreground = new SolidColorBrush(Color.FromRgb(220, 220, 200));
        private static readonly IPen Border = new Pen(Brushes.White);
        private DrawingContextImpl _context = null!;
        private ConsoloniaLifetime _lifetime = null!;
        private RenderTarget _renderTarget = null!;

        // the rows of two screens of text that differ in every cell, and a row with one character typed
        private IGlyphRunImpl[] _screenA = null!;
        private IGlyphRunImpl[] _screenB = null!;
        private IGlyphRunImpl _typedRow = null!;

        private IDisposable _scope = null!;
        private bool _toggle;
        private ConsoleWindowImpl _window = null!;

        [GlobalSetup]
        public void Setup()
        {
            var console = new UnitTestConsole(new PixelBufferSize(CellsWide, CellsHigh));
            _scope = AvaloniaLocator.EnterScope();
            _lifetime = ApplicationStartup.CreateLifetime(AppBuilder.Configure<BenchmarkApp>()
                .UseConsole(console)
                .UseConsolonia()
                .UseConsoleColorMode(new RgbConsoleColorMode())
                .WithConsoleFonts()
                .With<IPlatformSettings>(new ConsoloniaPlatformSettings
                {
                    UnsafeInput = false,
                    UnsafeRendering = false
                }), []);
            console.SetupLifetime(_lifetime);

            _window = new ConsoleWindowImpl();
            _renderTarget = new RenderTarget(new IPlatformRenderSurface[] { _window });
            _context = new DrawingContextImpl(_window, null);

            _screenA = Enumerable.Range(0, CellsHigh - 2).Select(row => ShapeRow(row, 0)).ToArray();
            _screenB = Enumerable.Range(0, CellsHigh - 2).Select(row => ShapeRow(row, 1)).ToArray();
            _typedRow = ShapeRow(CellsHigh / 2, 0, true);

            Frame(_screenA);
        }

        /// <summary>A row of text as a UI shows it: words of letters, a space every eight cells.</summary>
        private static IGlyphRunImpl ShapeRow(int row, int screen, bool typed = false)
        {
            char[] text = new char[CellsWide - 2];
            for (int i = 0; i < text.Length; i++)
                text[i] = i % 8 == 7 ? ' ' : (char)('a' + (i + row * 3 + screen * 13) % 26);
            if (typed) text[text.Length / 2] = '!';

            var platformRender = AvaloniaLocator.Current.GetRequiredService<IPlatformRenderInterface>();
            var textShaper = AvaloniaLocator.Current.GetRequiredService<ITextShaperImpl>();
            if (!FontManager.Current.TryGetGlyphTypeface(Typeface.Default, out GlyphTypeface typeface))
                throw new InvalidOperationException("Could not get GlyphTypeface");

            ShapedBuffer glyphs = textShaper.ShapeText(new string(text).AsMemory(),
                new TextShaperOptions(typeface, typeface.Metrics.DesignEmHeight));
            return platformRender.CreateGlyphRun(typeface, glyphs.FontRenderingEmSize, glyphs.ToList(), default);
        }

        private void Frame(IReadOnlyList<IGlyphRunImpl> rows, IGlyphRunImpl typedRow = null)
        {
            _context.Transform = Matrix.Identity;
            _context.DrawRectangle(Background, Border, new RoundedRect(new Rect(0, 0, CellsWide, CellsHigh)));
            for (int row = 0; row < rows.Count; row++)
            {
                _context.Transform = Matrix.CreateTranslation(1, row + 1);
                _context.DrawGlyphRun(Foreground,
                    typedRow != null && row == CellsHigh / 2 ? typedRow : rows[row]);
            }

            _renderTarget.RenderToDevice();
        }

        /// <summary>The screen drawn again as it was: every cell blended and compared, nothing written.</summary>
        [Benchmark(Baseline = true)]
        public void RedrawUnchanged()
        {
            Frame(_screenA);
        }

        /// <summary>One character typed in the middle of the screen: one cell written.</summary>
        [Benchmark]
        public void OneCharacterTyped()
        {
            _toggle = !_toggle;
            Frame(_screenA, _toggle ? _typedRow : null);
        }

        /// <summary>Every cell of text changed, as on a scroll: every text cell written.</summary>
        [Benchmark]
        public void EveryCellChanged()
        {
            _toggle = !_toggle;
            Frame(_toggle ? _screenB : _screenA);
        }

        [GlobalCleanup]
        public void Cleanup()
        {
            _renderTarget?.Dispose();
            _window?.Dispose();
            _lifetime?.Dispose();
            _scope?.Dispose();
        }

        private sealed class BenchmarkApp : Application;
    }
}
