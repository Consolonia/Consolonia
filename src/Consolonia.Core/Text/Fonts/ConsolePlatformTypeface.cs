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
            table = new ReadOnlyMemory<byte>([
                0x00, 0x01, 0x00, 0x00, // version = 1.0

                0x00, 0x01, // ascender = 1 → Ascent = -1
                0x00, 0x00, // descender = 0
                0x00, 0x00, // lineGap = 0

                0x00, 0x01, // advanceWidthMax = 1
                0x00, 0x00, // minLeftSideBearing
                0x00, 0x00, // minRightSideBearing
                0x00, 0x01, // xMaxExtent

                0x00, 0x01, // caretSlopeRise
                0x00, 0x00, // caretSlopeRun
                0x00, 0x00, // caretOffset

                0x00, 0x00, // reserved
                0x00, 0x00, // reserved
                0x00, 0x00, // reserved
                0x00, 0x00, // reserved

                0x00, 0x00, // metricDataFormat
                0x00, 0x01, // numberOfHMetrics
            ]);
            return true;

        case /* OS/2 */ 1330851634u:
        {
            var os2 = new byte[86];

            os2[1] = 0x01; // version = 1

            os2[2] = 0x00;
            os2[3] = 0x01; // xAvgCharWidth = 1

            os2[4] = 0x01;
            os2[5] = 0x90; // weightClass = 400

            os2[6] = 0x00;
            os2[7] = 0x05; // widthClass = 5

            os2[26] = (byte)(DrawingContextImpl.StrikethroughThickness >> 8);
            os2[27] = (byte)DrawingContextImpl.StrikethroughThickness;

            os2[28] = 0x00;
            os2[29] = 0x01; // strikeoutPosition = 1 → -1

            os2[62] = 0x00;
            os2[63] = 0xC0; // REGULAR | USE_TYPO_METRICS

            os2[68] = 0x00;
            os2[69] = 0x01; // typoAscender = 1

            os2[70] = 0x00;
            os2[71] = 0x00; // typoDescender = 0

            os2[72] = 0x00;
            os2[73] = 0x00; // typoLineGap = 0

            os2[74] = 0x00;
            os2[75] = 0x01; // winAscent = 1

            os2[76] = 0x00;
            os2[77] = 0x00; // winDescent = 0

            table = new ReadOnlyMemory<byte>(os2);
            return true;
        }

        case /* hmtx */ 1752003704u:
            table = new ReadOnlyMemory<byte>([
                0x00, 0x01, // advanceWidth = 1
                0x00, 0x00, // leftSideBearing = 0
            ]);
            return true;

        case /* head */ 1751474532u:
        {
            var head = new byte[54];

            head[0] = 0x00;
            head[1] = 0x01; // version = 1.0

            head[12] = 0x5F;
            head[13] = 0x0F;
            head[14] = 0x3C;
            head[15] = 0xF5; // magicNumber

            head[18] = 0x00;
            head[19] = 0x01; // unitsPerEm = 1

            table = new ReadOnlyMemory<byte>(head);
            return true;
        }

        case /* post */ 1886352244u:
        {
            var post = new byte[32];

            post[0] = 0x00;
            post[1] = 0x03; // version = 3.0

            post[8] = 0x00;
            post[9] = 0x01; // underlinePosition = 1 → -1

            post[10] = DrawingContextImpl.UnderlineThickness >> 8;
            post[11] = DrawingContextImpl.UnderlineThickness;

            post[15] = 0x01; // isFixedPitch = true

            table = new ReadOnlyMemory<byte>(post);
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
