using System;
using System.IO;
using System.Text;
using Avalonia.Media;
using Avalonia.Media.Fonts;

namespace Consolonia.Core.Text.Fonts
{
    internal sealed class ConsolePlatformTypeface(IConsoleTypeface consoleTypeface) : IPlatformTypeface
    {
        public IConsoleTypeface ConsoleTypeface { get; } = consoleTypeface;

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
                case /* name */ 1851878757u:
                {
                    byte[] familyNameBytes = Encoding.BigEndianUnicode.GetBytes(ConsoleTypeface.FamilyName);
                    string subfamilyName = ConsoleTypeface.Style != FontStyle.Normal
                        ? ConsoleTypeface.Style.ToString()
                        : ConsoleTypeface.Weight != FontWeight.Normal
                            ? ConsoleTypeface.Weight.ToString()
                            : "Regular";
                    byte[] subfamilyNameBytes = Encoding.BigEndianUnicode.GetBytes(subfamilyName);
                    string fullName = $"{ConsoleTypeface.FamilyName} {subfamilyName}";
                    byte[] fullNameBytes = Encoding.BigEndianUnicode.GetBytes(fullName);

                    const int nameRecordsCount = 3;
                    const int stringOffset = 6 + nameRecordsCount * 12;
                    byte[] nameTable = new byte[stringOffset + familyNameBytes.Length + subfamilyNameBytes.Length +
                                                fullNameBytes.Length];

                    // Header
                    nameTable[1] = 0x00; // format = 0
                    nameTable[2] = 0;
                    nameTable[3] = nameRecordsCount;
                    nameTable[4] = 0;
                    nameTable[5] = stringOffset;

                    int currentStringOffset = 0;

                    // NameRecord 1: Family Name
                    AddNameRecord(nameTable, 6, 1, familyNameBytes.Length, currentStringOffset);
                    Array.Copy(familyNameBytes, 0, nameTable, stringOffset,
                        familyNameBytes.Length);
                    currentStringOffset += familyNameBytes.Length;

                    // NameRecord 2: Subfamily Name
                    AddNameRecord(nameTable, 18, 2, subfamilyNameBytes.Length, currentStringOffset);
                    Array.Copy(subfamilyNameBytes, 0, nameTable, stringOffset + currentStringOffset,
                        subfamilyNameBytes.Length);
                    currentStringOffset += subfamilyNameBytes.Length;

                    // NameRecord 3: Full Name
                    AddNameRecord(nameTable, 30, 4, fullNameBytes.Length, currentStringOffset);
                    Array.Copy(fullNameBytes, 0, nameTable, stringOffset + currentStringOffset, fullNameBytes.Length);

                    table = nameTable;
                    return true;

                    static void AddNameRecord(byte[] bytes, int recordOffset, ushort nameId, int length,
                        int stringOffsetInsideStorage)
                    {
                        bytes[recordOffset + 1] = 0x03; // platformID = Windows
                        bytes[recordOffset + 3] = 0x01; // encodingID = Unicode BMP
                        bytes[recordOffset + 4] = 0x04;
                        bytes[recordOffset + 5] = 0x09; // languageID = English - US
                        bytes[recordOffset + 6] = (byte)(nameId >> 8);
                        bytes[recordOffset + 7] = (byte)nameId;
                        bytes[recordOffset + 8] = (byte)(length >> 8);
                        bytes[recordOffset + 9] = (byte)length;
                        bytes[recordOffset + 10] = (byte)(stringOffsetInsideStorage >> 8);
                        bytes[recordOffset + 11] = (byte)stringOffsetInsideStorage;
                    }
                }
                case /* cmap */ 1668112752u:
                    table = new ReadOnlyMemory<byte>([
                        0x00, 0x00, // version = 0
                        0x00, 0x01, // numTables = 1

                        0x00, 0x03, // platformID = Windows
                        0x00, 0x0A, // encodingID = full Unicode
                        0x00, 0x00, 0x00, 0x0C, // subtableOffset = 12

                        0x00, 0x0C, // format = 12
                        0x00, 0x00, // reserved
                        0x00, 0x00, 0x00, 0x10, // length = 16
                        0x00, 0x00, 0x00, 0x00, // language = 0
                        0x00, 0x00, 0x00, 0x00 // numGroups = 0
                    ]);
                    return true;

                case /* maxp */ 1835104368u:
                    table = new ReadOnlyMemory<byte>([
                        0x00, 0x00, 0x50, 0x00, // version = 0.5
                        0x00, 0x01 // numGlyphs = 1
                    ]);
                    return true;

                case /* hhea */ 1751672161u:
                {
                    byte[] hhea = new byte[36];
                    hhea[1] = 0x01; // version = 1.0

                    short ascender = (short)-metrics.Ascent;
                    hhea[4] = (byte)(ascender >> 8);
                    hhea[5] = (byte)ascender;

                    short descender = (short)-metrics.Descent;
                    hhea[6] = (byte)(descender >> 8);
                    hhea[7] = (byte)descender;

                    short lineGap = (short)metrics.LineGap;
                    hhea[8] = (byte)(lineGap >> 8);
                    hhea[9] = (byte)lineGap;

                    ushort advanceWidthMax = metrics.DesignEmHeight;
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
                    byte[] os2 = new byte[86];

                    os2[1] = 0x01; // version = 1

                    short xAvgCharWidth = (short)metrics.DesignEmHeight;
                    os2[2] = (byte)(xAvgCharWidth >> 8);
                    os2[3] = (byte)xAvgCharWidth;

                    ushort weightClass = (ushort)ConsoleTypeface.Weight;
                    os2[4] = (byte)(weightClass >> 8);
                    os2[5] = (byte)weightClass;

                    os2[7] = 0x05; // widthClass = 5

                    short strikethroughThickness = (short)metrics.StrikethroughThickness;
                    os2[26] = (byte)(strikethroughThickness >> 8);
                    os2[27] = (byte)strikethroughThickness;

                    short strikethroughPosition = (short)-metrics.StrikethroughPosition;
                    os2[28] = (byte)(strikethroughPosition >> 8);
                    os2[29] = (byte)strikethroughPosition;

                    os2[63] = 0xC0; // REGULAR | USE_TYPO_METRICS

                    short typoAscender = (short)-metrics.Ascent;
                    os2[68] = (byte)(typoAscender >> 8);
                    os2[69] = (byte)typoAscender;

                    short typoDescender = (short)-metrics.Descent;
                    os2[70] = (byte)(typoDescender >> 8);
                    os2[71] = (byte)typoDescender;

                    short typoLineGap = (short)metrics.LineGap;
                    os2[72] = (byte)(typoLineGap >> 8);
                    os2[73] = (byte)typoLineGap;

                    ushort winAscent = (ushort)-metrics.Ascent;
                    os2[74] = (byte)(winAscent >> 8);
                    os2[75] = (byte)winAscent;

                    ushort winDescent = (ushort)metrics.Descent;
                    os2[76] = (byte)(winDescent >> 8);
                    os2[77] = (byte)winDescent;

                    table = os2;
                    return true;
                }

                case /* hmtx */ 1752003704u:
                {
                    ushort advanceWidth = metrics.DesignEmHeight;
                    table = new ReadOnlyMemory<byte>([
                        (byte)(advanceWidth >> 8), (byte)advanceWidth, // advanceWidth
                        0x00, 0x00 // leftSideBearing = 0
                    ]);
                    return true;
                }

                case /* head */ 1751474532u:
                {
                    byte[] head = new byte[54];

                    head[1] = 0x01; // version = 1.0

                    head[12] = 0x5F;
                    head[13] = 0x0F;
                    head[14] = 0x3C;
                    head[15] = 0xF5; // magicNumber

                    ushort unitsPerEm = metrics.DesignEmHeight;
                    head[18] = (byte)(unitsPerEm >> 8);
                    head[19] = (byte)unitsPerEm;

                    table = head;
                    return true;
                }

                case /* post */ 1886352244u:
                {
                    byte[] post = new byte[32];

                    post[1] = 0x03; // version = 3.0

                    short underlinePosition = (short)-metrics.UnderlinePosition;
                    post[8] = (byte)(underlinePosition >> 8);
                    post[9] = (byte)underlinePosition;

                    short underlineThickness = (short)metrics.UnderlineThickness;
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