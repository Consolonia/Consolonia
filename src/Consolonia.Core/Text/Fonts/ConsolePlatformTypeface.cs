using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using Avalonia.Media;
using Avalonia.Media.Fonts;

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
                case /*"cmap"*/1668112752u:

                    // Format 12 — full Unicode mapping, but dummy, no actual page
                    table = new ReadOnlyMemory<byte>([
                        0x00, 0x00, // version = 0
                        0x00, 0x01, // numTables = 1

                        // EncodingRecord
                        0x00, 0x03, // platformID = 3: Windows
                        0x00, 0x0A, // encodingID = 10: full Unicode
                        0x00, 0x00, 0x00, 0x0C, // subtableOffset = 12

                        // Format 12 subtable
                        0x00, 0x0C, // format = 12
                        0x00, 0x00, // reserved
                        0x00, 0x00, 0x00, 0x10, // length = 16 bytes
                        0x00, 0x00, 0x00, 0x00, // language = 0
                        0x00, 0x00, 0x00, 0x00, // numGroups = 0
                    ]);
                    return true;
                case /*maxp*/ 1835104368u:
                    // also dummy, just to calm down Avalonia
                    table = new ReadOnlyMemory<byte>(new byte[12]);
                    return true;
            }
            /*if (_fallbackTypeface is IFontMemory fontMemory &&
                fontMemory.TryGetTable(tag, out table))
            {
                table = NormalizeConsoleMetricTable(tag, table);
                return true;
            }
            */

            table = default;
            return false;
        }

        public void Dispose()
        {
            ConsoleTypeface?.Dispose();
        }
    }
}
