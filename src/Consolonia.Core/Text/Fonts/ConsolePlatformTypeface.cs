using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using Avalonia.Media;
using Avalonia.Media.Fonts;
using Consolonia.Core.Drawing;

namespace Consolonia.Core.Text.Fonts
{
    internal sealed class ConsolePlatformTypeface : IPlatformTypeface
    {
        public ConsolePlatformTypeface(IConsoleTypeface consoleTypeface)
        {
            ConsoleTypeface = consoleTypeface;
        }

        public IConsoleTypeface ConsoleTypeface { get; }

        public string FamilyName => ConsoleTypeface.FamilyName;

        public FontWeight Weight => ConsoleTypeface.Weight;

        public FontStyle Style => ConsoleTypeface.Style;

        public FontStretch Stretch => ConsoleTypeface.Stretch;

        public FontSimulations FontSimulations => ConsoleTypeface.FontSimulations;
        
        public bool TryGetStream(out Stream stream)
        {
            stream = null;
            return false;
        }

        public bool TryGetTable(OpenTypeTag tag, out ReadOnlyMemory<byte> table)
        {
            FontMetrics metrics = ConsoleTypeface.Metrics;

            switch (tag)
            {
        case /* cmap */ 1668112752u:
            table = new ReadOnlyMemory<byte>([
                0x00, 0x00,             // version = 0
                0x00, 0x01,             // numTables = 1

                0x00, 0x03,             // platformID = Windows
                0x00, 0x0A,             // encodingID = full Unicode
                0x00, 0x00, 0x00, 0x0C, // subtableOffset = 12

                0x00, 0x0C,             // format = 12
                0x00, 0x00,             // reserved
                0x00, 0x00, 0x00, 0x10, // length = 16
                0x00, 0x00, 0x00, 0x00, // language = 0
                0x00, 0x00, 0x00, 0x00, // numGroups = 0
            ]);
            return true;

        case /* maxp */ 1835104368u:
            table = new ReadOnlyMemory<byte>([
                0x00, 0x00, 0x50, 0x00, // version = 0.5
                0x00, 0x01,             // numGlyphs = 1
            ]);
            return true;

        case /* hhea */ 1751672161u:
        {
            var hhea = new byte[36];
            hhea[1] = 0x01; // version = 1.0

            var ascender = (short)-metrics.Ascent;
            hhea[4] = (byte)(ascender >> 8);
            hhea[5] = (byte)ascender;

            var descender = (short)-metrics.Descent;
            hhea[6] = (byte)(descender >> 8);
            hhea[7] = (byte)descender;

            var lineGap = (short)metrics.LineGap;
            hhea[8] = (byte)(lineGap >> 8);
            hhea[9] = (byte)lineGap;

            var advanceWidthMax = (ushort)metrics.DesignEmHeight;
            hhea[10] = (byte)(advanceWidthMax >> 8);
            hhea[11] = (byte)advanceWidthMax;

            hhea[16] = (byte)(advanceWidthMax >> 8);
            hhea[17] = (byte)advanceWidthMax; // xMaxExtent

            hhea[19] = 0x01; // caretSlopeRise

            hhea[35] = 0x01; // numberOfHMetrics
            table = hhea;
            return true;
        }

        case /* OS/2 */ 1330851634u:
        {
            var os2 = new byte[86];

            os2[1] = 0x01; // version = 1

            var xAvgCharWidth = (short)metrics.DesignEmHeight;
            os2[2] = (byte)(xAvgCharWidth >> 8);
            os2[3] = (byte)xAvgCharWidth;

            var weightClass = (ushort)ConsoleTypeface.Weight;
            os2[4] = (byte)(weightClass >> 8);
            os2[5] = (byte)weightClass;

            os2[7] = 0x05; // widthClass = 5

            var strikethroughThickness = (short)metrics.StrikethroughThickness;
            os2[26] = (byte)(strikethroughThickness >> 8);
            os2[27] = (byte)strikethroughThickness;

            var strikethroughPosition = (short)-metrics.StrikethroughPosition;
            os2[28] = (byte)(strikethroughPosition >> 8);
            os2[29] = (byte)strikethroughPosition;

            os2[63] = 0xC0; // REGULAR | USE_TYPO_METRICS

            var typoAscender = (short)-metrics.Ascent;
            os2[68] = (byte)(typoAscender >> 8);
            os2[69] = (byte)typoAscender;

            var typoDescender = (short)-metrics.Descent;
            os2[70] = (byte)(typoDescender >> 8);
            os2[71] = (byte)typoDescender;

            var typoLineGap = (short)metrics.LineGap;
            os2[72] = (byte)(typoLineGap >> 8);
            os2[73] = (byte)typoLineGap;

            var winAscent = (ushort)-metrics.Ascent;
            os2[74] = (byte)(winAscent >> 8);
            os2[75] = (byte)winAscent;

            var winDescent = (ushort)metrics.Descent;
            os2[76] = (byte)(winDescent >> 8);
            os2[77] = (byte)winDescent;

            table = os2;
            return true;
        }

        case /* hmtx */ 1752003704u:
        {
            var advanceWidth = (ushort)metrics.DesignEmHeight;
            table = new ReadOnlyMemory<byte>([
                (byte)(advanceWidth >> 8), (byte)advanceWidth, // advanceWidth
                0x00, 0x00 // leftSideBearing = 0
            ]);
            return true;
        }

        case /* head */ 1751474532u:
        {
            var head = new byte[54];

            head[1] = 0x01; // version = 1.0

            head[12] = 0x5F;
            head[13] = 0x0F;
            head[14] = 0x3C;
            head[15] = 0xF5; // magicNumber

            var unitsPerEm = (ushort)metrics.DesignEmHeight;
            head[18] = (byte)(unitsPerEm >> 8);
            head[19] = (byte)unitsPerEm;

            table = head;
            return true;
        }

        case /* post */ 1886352244u:
        {
            var post = new byte[32];

            post[1] = 0x03; // version = 3.0

            var underlinePosition = (short)-metrics.UnderlinePosition;
            post[8] = (byte)(underlinePosition >> 8);
            post[9] = (byte)underlinePosition;

            var underlineThickness = (short)metrics.UnderlineThickness;
            post[10] = (byte)(underlineThickness >> 8);
            post[11] = (byte)underlineThickness;

            post[15] = metrics.IsFixedPitch ? (byte)0x01 : (byte)0x00;

            table = post;
            return true;
        }

        default:
            table = default;
            return false;
    }
}
        public void Dispose()
        {
            ConsoleTypeface?.Dispose();
        }
    }
}
