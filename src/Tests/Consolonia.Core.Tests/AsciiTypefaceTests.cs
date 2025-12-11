using System;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Consolonia.Controls;
using Consolonia.Core.Text.Fonts;
using NUnit.Framework;

namespace Consolonia.Core.Tests
{
    [TestFixture]
    public class AsciiArtTypefaceTests
    {
        [SetUp]
        public void Setup()
        {
            AvaloniaLocator.CurrentMutable.Bind<IConsoleCapabilities>().ToConstant(new MockConsoleCapabilities() { Capabilities=ConsoleCapabilities.SupportsComplexEmoji});
        }

        [Test]
        public void ConstructorWithFamilyNameCreatesTypeface()
        {
            // Arrange & Act
            var typeface = new AsciiArtTypeface("TestFont");

            // Assert
            Assert.AreEqual("TestFont", typeface.FamilyName);
            Assert.AreEqual(FontWeight.Normal, typeface.Weight);
            Assert.AreEqual(FontStyle.Normal, typeface.Style);
            Assert.AreEqual(FontStretch.Normal, typeface.Stretch);
            Assert.AreEqual(FontSimulations.None, typeface.FontSimulations);
            Assert.AreEqual(0, typeface.GlyphCount);
        }

        [Test]
        public void AddGlyphAddsGlyphAndReturnsIndex()
        {
            // Arrange
            var typeface = new AsciiArtTypeface("TestFont");
            uint codepoint = 'A';
            var glyph = new AsciiArtGlyph(typeface, codepoint, ["***", "* *", "***"]);

            // Act
            ushort index = typeface.AddGlyph(codepoint, glyph);

            // Assert
            Assert.AreEqual(0, index);
            Assert.AreEqual(1, typeface.GlyphCount);
        }

        [Test]
        public void AddGlyphWithSpaceAlsoCreatesTabGlyph()
        {
            // Arrange
            var typeface = new AsciiArtTypeface("TestFont");
            uint spaceCodepoint = ' ';
            var glyph = new AsciiArtGlyph(typeface, spaceCodepoint, [" ", " ", " "]);

            // Act
            typeface.AddGlyph(spaceCodepoint, glyph);

            // Assert
            Assert.AreEqual(2, typeface.GlyphCount); // Space + Tab
            ushort tabGlyph = typeface.GetGlyph('\t');
            Assert.AreNotEqual(ushort.MaxValue, tabGlyph);
        }

        [Test]
        public void AddGlyphMultipleGlyphsReturnsIncrementingIndices()
        {
            // Arrange
            var typeface = new AsciiArtTypeface("TestFont");
            var glyph1 = new AsciiArtGlyph(typeface, 'A', ["A"]);
            var glyph2 = new AsciiArtGlyph(typeface, 'B', ["B"]);
            var glyph3 = new AsciiArtGlyph(typeface, 'C', ["C"]);

            // Act
            ushort index1 = typeface.AddGlyph('A', glyph1);
            ushort index2 = typeface.AddGlyph('B', glyph2);
            ushort index3 = typeface.AddGlyph('C', glyph3);

            // Assert
            Assert.AreEqual(0, index1);
            Assert.AreEqual(1, index2);
            Assert.AreEqual(2, index3);
            Assert.AreEqual(3, typeface.GlyphCount);
        }

        [Test]
        public void GetGlyphWithExistingCodepointReturnsGlyphIndex()
        {
            // Arrange
            var typeface = new AsciiArtTypeface("TestFont");
            uint codepoint = 'A';
            var glyph = new AsciiArtGlyph(typeface, codepoint, ["A"]);
            ushort expectedIndex = typeface.AddGlyph(codepoint, glyph);

            // Act
            ushort index = typeface.GetGlyph(codepoint);

            // Assert
            Assert.AreEqual(expectedIndex, index);
        }

        [Test]
        public void GetGlyphWithNonExistingCodepointReturnsFallbackGlyph()
        {
            // Arrange
            var typeface = new AsciiArtTypeface("TestFont")
            {
                Metrics = new FontMetrics { DesignEmHeight = 5 }
            };

            // Act
            ushort index = typeface.GetGlyph('Z');

            // Assert
            Assert.AreNotEqual(ushort.MaxValue, index); // Should create a fallback glyph
        }

        [Test]
        public void TryGetGlyphWithExistingCodepointReturnsTrue()
        {
            // Arrange
            var typeface = new AsciiArtTypeface("TestFont");
            var glyph = new AsciiArtGlyph(typeface, 'A', ["A"]);
            typeface.AddGlyph('A', glyph);

            // Act
            bool result = typeface.TryGetGlyph('A', out ushort glyphIndex);

            // Assert
            Assert.IsTrue(result);
            Assert.AreNotEqual(ushort.MaxValue, glyphIndex);
        }

        [Test]
        public void TryGetGlyphWithNonExistingCodepointCreatesFallback()
        {
            // Arrange
            var typeface = new AsciiArtTypeface("TestFont")
            {
                Metrics = new FontMetrics { DesignEmHeight = 5 }
            };

            // Act
            bool result = typeface.TryGetGlyph('Z', out ushort glyphIndex);

            // Assert
            Assert.IsTrue(result); // Always returns true, creates fallback if needed
            Assert.AreNotEqual(ushort.MaxValue, glyphIndex);
        }

        [Test]
        public void GetGlyphAdvanceReturnsCorrectWidth()
        {
            // Arrange
            var typeface = new AsciiArtTypeface("TestFont");
            var glyph = new AsciiArtGlyph(typeface, 'A', ["***"]);
            ushort glyphIndex = typeface.AddGlyph('A', glyph);

            // Act
            int advance = typeface.GetGlyphAdvance(glyphIndex);

            // Assert
            Assert.AreEqual(3, advance);
        }

        [Test]
        public void GetGlyphAdvanceWithInvalidIndexReturnsZero()
        {
            // Arrange
            var typeface = new AsciiArtTypeface("TestFont");

            // Act
            int advance = typeface.GetGlyphAdvance(ushort.MaxValue);

            // Assert
            Assert.AreEqual(0, advance);
        }

        [Test]
        public void GetGlyphAdvancesReturnsArrayOfAdvances()
        {
            // Arrange
            var typeface = new AsciiArtTypeface("TestFont");
            typeface.AddGlyph('A', new AsciiArtGlyph(typeface, 'A', ["*"]));
            typeface.AddGlyph('B', new AsciiArtGlyph(typeface, 'B', ["**"]));
            typeface.AddGlyph('C', new AsciiArtGlyph(typeface, 'C', ["***"]));

            ushort[] glyphs = [0, 1, 2];

            // Act
            int[] advances = typeface.GetGlyphAdvances(glyphs);

            // Assert
            Assert.AreEqual(3, advances.Length);
            Assert.AreEqual(1, advances[0]);
            Assert.AreEqual(2, advances[1]);
            Assert.AreEqual(3, advances[2]);
        }

        [Test]
        public void GetGlyphsReturnsArrayOfGlyphIndices()
        {
            // Arrange
            var typeface = new AsciiArtTypeface("TestFont");
            typeface.AddGlyph('A', new AsciiArtGlyph(typeface, 'A', ["A"]));
            typeface.AddGlyph('B', new AsciiArtGlyph(typeface, 'B', ["B"]));

            uint[] codepoints = ['A', 'B'];

            // Act
            ushort[] glyphs = typeface.GetGlyphs(codepoints);

            // Assert
            Assert.AreEqual(2, glyphs.Length);
            Assert.AreEqual(0, glyphs[0]);
            Assert.AreEqual(1, glyphs[1]);
        }

        [Test]
        public void TryGetGlyphMetricsReturnsCorrectMetrics()
        {
            // Arrange
            var typeface = new AsciiArtTypeface("TestFont")
            {
                Metrics = new FontMetrics { DesignEmHeight = 5 }
            };
            var glyph = new AsciiArtGlyph(typeface, 'A', ["***", "* *", "***"]);
            ushort glyphIndex = typeface.AddGlyph('A', glyph);

            // Act
            bool result = typeface.TryGetGlyphMetrics(glyphIndex, out GlyphMetrics metrics);

            // Assert
            Assert.IsTrue(result);
            Assert.AreEqual(0, metrics.XBearing);
            Assert.AreEqual(0, metrics.YBearing);
            Assert.AreEqual(5, metrics.Height);
            Assert.AreEqual(3, metrics.Width);
        }

        [Test]
        public void TryGetTableReturnsFalse()
        {
            // Arrange
            var typeface = new AsciiArtTypeface("TestFont");

            // Act
            bool result = typeface.TryGetTable(0, out byte[] table);

            // Assert
            Assert.IsFalse(result);
            Assert.IsTrue(table.Length == 0);
        }

        [Test]
        public void ShapeTextWithoutKerningReturnsFullWidthAdvances()
        {
            // Arrange
            var typeface = new AsciiArtTypeface("TestFont")
            {
                Metrics = new FontMetrics { DesignEmHeight = 5 },
                LayoutMode = LayoutMode.None
            };
            typeface.AddGlyph('A', new AsciiArtGlyph(typeface, 'A', ["***"]));
            typeface.AddGlyph('B', new AsciiArtGlyph(typeface, 'B', ["**"]));

            var options = new TextShaperOptions(typeface, 1);

            // Act
            ShapedBuffer result = typeface.ShapeText("AB".AsMemory(), options);

            // Assert
            Assert.AreEqual(2, result.Length);
            Assert.AreEqual(3, result[0].GlyphAdvance); // A is 3 wide
            Assert.AreEqual(2, result[1].GlyphAdvance); // B is 2 wide
        }

        [Test]
        public void ShapeTextWithKerningReturnsAdjustedAdvances()
        {
            // Arrange
            var typeface = new AsciiArtTypeface("TestFont")
            {
                Metrics = new FontMetrics { DesignEmHeight = 3 },
                LayoutMode = LayoutMode.Kern
            };
            // "A " followed by " B" should kern closer
            typeface.AddGlyph('A', new AsciiArtGlyph(typeface, 'A', ["*  ", "*  ", "*  "]));
            typeface.AddGlyph('B', new AsciiArtGlyph(typeface, 'B', ["  *", "  *", "  *"]));

            var options = new TextShaperOptions(typeface, 1);

            // Act
            ShapedBuffer result = typeface.ShapeText("AB".AsMemory(), options);

            // Assert
            Assert.AreEqual(2, result.Length);
            // With kerning, the advance should be less than full width
            Assert.Less(result[0].GlyphAdvance, 3);
        }

        [Test]
        public void CalculateAdvancesWithKerningWithEmptyArrayReturnsEmpty()
        {
            // Arrange
            var typeface = new AsciiArtTypeface("TestFont");
            AsciiArtGlyph[] glyphs = [];

            // Act
            byte[] advances = typeface.CalculateAdvancesWithKerning(glyphs);

            // Assert
            Assert.IsEmpty(advances);
        }

        [Test]
        public void CalculateAdvancesWithKerningWithSingleGlyphReturnsFullWidth()
        {
            // Arrange
            var typeface = new AsciiArtTypeface("TestFont");
            var glyph = new AsciiArtGlyph(typeface, 'A', ["***"]);
            AsciiArtGlyph[] glyphs = [glyph];

            // Act
            byte[] advances = typeface.CalculateAdvancesWithKerning(glyphs);

            // Assert
            Assert.AreEqual(1, advances.Length);
            Assert.AreEqual(3, advances[0]);
        }

        [Test]
        public void CalculateAdvancesWithKerningWithSpaceNoKerning()
        {
            // Arrange
            var typeface = new AsciiArtTypeface("TestFont");
            var glyphA = new AsciiArtGlyph(typeface, 'A', ["***"]);
            var glyphSpace = new AsciiArtGlyph(typeface, ' ', ["   "]);
            AsciiArtGlyph[] glyphs = [glyphA, glyphSpace];

            // Act
            byte[] advances = typeface.CalculateAdvancesWithKerning(glyphs);

            // Assert
            Assert.AreEqual(2, advances.Length);
            Assert.AreEqual(3, advances[0]); // Full width before space
            Assert.AreEqual(3, advances[1]);
        }

        [Test]
        public void CalculateAdvancesWithKerningWithOverlapReducesAdvance()
        {
            // Arrange
            var typeface = new AsciiArtTypeface("TestFont")
            {
                LayoutMode = LayoutMode.Kern
            };
            // Glyph with trailing space
            var glyphA = new AsciiArtGlyph(typeface, 'A', ["*  "]);
            // Glyph with leading space
            var glyphB = new AsciiArtGlyph(typeface, 'B', ["  *"]);
            AsciiArtGlyph[] glyphs = [glyphA, glyphB];

            // Act
            byte[] advances = typeface.CalculateAdvancesWithKerning(glyphs);

            // Assert
            Assert.AreEqual(2, advances.Length);
            Assert.Less(advances[0], 3); // Should be kerned
            Assert.AreEqual(3, advances[1]); // Last glyph is full width
        }

        [Test]
        public void LayoutModeDefaultValueIsNone()
        {
            // Arrange & Act
            var typeface = new AsciiArtTypeface("TestFont");

            // Assert
            Assert.AreEqual(LayoutMode.None, typeface.LayoutMode);
        }

        [Test]
        public void LayoutModeCanBeSet()
        {
            // Arrange
            var typeface = new AsciiArtTypeface("TestFont")
            {
                // Act
                LayoutMode = LayoutMode.Kern | LayoutMode.Smush
            };

            // Assert
            Assert.IsTrue(typeface.LayoutMode.HasFlag(LayoutMode.Kern));
            Assert.IsTrue(typeface.LayoutMode.HasFlag(LayoutMode.Smush));
        }

        [Test]
        public void OldLayoutModeDefaultValueIsNone()
        {
            // Arrange & Act
            var typeface = new AsciiArtTypeface("TestFont");

            // Assert
            Assert.AreEqual(OldLayoutMode.None, typeface.OldLayoutMode);
        }

        [Test]
        public void HardblankDefaultValueIsDollarSign()
        {
            // Arrange & Act
            var typeface = new AsciiArtTypeface("TestFont");

            // Assert
            Assert.AreEqual('$', typeface.Hardblank);
        }

        [Test]
        public void HardblankCanBeSet()
        {
            // Arrange & Act
            var typeface = new AsciiArtTypeface("TestFont")
            {
                Hardblank = '#'
            };

            // Assert
            Assert.AreEqual('#', typeface.Hardblank);
        }

        [Test]
        public void DisposeDoesNotThrow()
        {
            // Arrange
            var typeface = new AsciiArtTypeface("TestFont");

            // Act & Assert
            Assert.DoesNotThrow(() => typeface.Dispose());
        }

        [Test]
        public void DisposeMultipleTimesDoesNotThrow()
        {
            // Arrange
            var typeface = new AsciiArtTypeface("TestFont");

            // Act & Assert
            Assert.DoesNotThrow(() =>
            {
                typeface.Dispose();
                typeface.Dispose();
            });
        }

        [Test]
        public void MetricsCanBeSetAndRetrieved()
        {
            // Arrange
            var typeface = new AsciiArtTypeface("TestFont");
            var metrics = new FontMetrics
            {
                DesignEmHeight = 10,
                Ascent = 8,
                Descent = 2
            };

            // Act
            typeface.Metrics = metrics;

            // Assert
            Assert.AreEqual(10, typeface.Metrics.DesignEmHeight);
            Assert.AreEqual(8, typeface.Metrics.Ascent);
            Assert.AreEqual(2, typeface.Metrics.Descent);
        }

        [Test]
        public void ShapeTextWithComplexGraphemesHandlesCorrectly()
        {
            // Arrange
            var typeface = new AsciiArtTypeface("TestFont")
            {
                Metrics = new FontMetrics { DesignEmHeight = 3 }
            };
            typeface.AddGlyph('A', new AsciiArtGlyph(typeface, 'A', ["*"]));

            var options = new TextShaperOptions(typeface, 1);

            // Act
            ShapedBuffer result = typeface.ShapeText("A".AsMemory(), options);

            // Assert
            Assert.AreEqual(1, result.Length);
            Assert.AreEqual(0, result[0].GlyphIndex);
        }

        [TestCase(LayoutMode.Equal)]
        [TestCase(LayoutMode.Lowline)]
        [TestCase(LayoutMode.Hierarchy)]
        [TestCase(LayoutMode.Pair)]
        [TestCase(LayoutMode.BigX)]
        [TestCase(LayoutMode.Hardblank)]
        public void LayoutModeIndividualFlagsCanBeSet(LayoutMode mode)
        {
            // Arrange & Act
            var typeface = new AsciiArtTypeface("TestFont")
            {
                LayoutMode = mode | LayoutMode.Smush
            };

            // Assert
            Assert.IsTrue(typeface.LayoutMode.HasFlag(mode));
            Assert.IsTrue(typeface.LayoutMode.HasFlag(LayoutMode.Smush));
        }

        [Test]
        public void PropertiesInitializedCorrectly()
        {
            // Arrange & Act
            var typeface = new AsciiArtTypeface("CustomFont")
            {
                Weight = FontWeight.Bold,
                Style = FontStyle.Italic,
                Stretch = FontStretch.Condensed,
                FontSimulations = FontSimulations.Bold
            };

            // Assert
            Assert.AreEqual("CustomFont", typeface.FamilyName);
            Assert.AreEqual(FontWeight.Bold, typeface.Weight);
            Assert.AreEqual(FontStyle.Italic, typeface.Style);
            Assert.AreEqual(FontStretch.Condensed, typeface.Stretch);
            Assert.AreEqual(FontSimulations.Bold, typeface.FontSimulations);
        }
    }
}