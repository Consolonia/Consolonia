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
        private readonly IPlatformTypeface _fallbackTypeface;

        public ConsolePlatformTypeface(IConsoleTypeface consoleTypeface, IPlatformTypeface fallbackTypeface)
        {
            ConsoleTypeface = consoleTypeface;
            _fallbackTypeface = fallbackTypeface;
        }

        public IConsoleTypeface ConsoleTypeface { get; }

        public string FamilyName => ConsoleTypeface.FamilyName;

        public FontWeight Weight => ConsoleTypeface.Weight;

        public FontStyle Style => ConsoleTypeface.Style;

        public FontStretch Stretch => ConsoleTypeface.Stretch;

        public FontSimulations FontSimulations => ConsoleTypeface.FontSimulations;

        internal static IPlatformTypeface CreateSystemFontFallbackTypeface(IConsoleTypeface consoleTypeface)
        {
            // Avalonia 12 requires OpenType metadata to construct GlyphTypeface. Consolonia still renders
            // through IConsoleTypeface; these platform fonts only provide metric tables and streams.
            return SystemFontFallbackTypeface.Create(consoleTypeface);
        }

        public bool TryGetStream(out Stream stream)
        {
            return _fallbackTypeface.TryGetStream(out stream);
        }

        public bool TryGetTable(OpenTypeTag tag, out ReadOnlyMemory<byte> table)
        {
            if (_fallbackTypeface is IFontMemory fontMemory &&
                fontMemory.TryGetTable(tag, out table))
            {
                table = NormalizeConsoleMetricTable(tag, table);
                return true;
            }

            table = default;
            return false;
        }

        private static ReadOnlyMemory<byte> NormalizeConsoleMetricTable(OpenTypeTag tag, ReadOnlyMemory<byte> table)
        {
            switch (tag.ToString())
            {
                case "head":
                    return NormalizeHeadTable(table);
                case "hhea":
                    return NormalizeHorizontalHeaderTable(table);
                case "OS/2":
                    return NormalizeOs2Table(table);
                case "post":
                    return NormalizePostTable(table);
                default:
                    return table;
            }
        }

        private static ReadOnlyMemory<byte> NormalizeHeadTable(ReadOnlyMemory<byte> table)
        {
            if (table.Length < 20)
                return table;

            byte[] bytes = table.ToArray();
            WriteUInt16(bytes, 18, 1);
            return bytes;
        }

        private static ReadOnlyMemory<byte> NormalizeHorizontalHeaderTable(ReadOnlyMemory<byte> table)
        {
            if (table.Length < 10)
                return table;

            byte[] bytes = table.ToArray();
            WriteInt16(bytes, 4, 1);
            WriteInt16(bytes, 6, 0);
            WriteInt16(bytes, 8, 0);
            return bytes;
        }

        private static ReadOnlyMemory<byte> NormalizeOs2Table(ReadOnlyMemory<byte> table)
        {
            if (table.Length < 78)
                return table;

            byte[] bytes = table.ToArray();
            WriteInt16(bytes, 26, 1);
            WriteInt16(bytes, 28, -1);
            WriteInt16(bytes, 68, 1);
            WriteInt16(bytes, 70, 0);
            WriteInt16(bytes, 72, 0);
            WriteUInt16(bytes, 74, 1);
            WriteUInt16(bytes, 76, 0);
            return bytes;
        }

        private static ReadOnlyMemory<byte> NormalizePostTable(ReadOnlyMemory<byte> table)
        {
            if (table.Length < 12)
                return table;

            byte[] bytes = table.ToArray();
            WriteInt16(bytes, 8, -1);
            WriteInt16(bytes, 10, 1);
            return bytes;
        }

        private static void WriteInt16(byte[] bytes, int offset, short value)
        {
            WriteUInt16(bytes, offset, unchecked((ushort)value));
        }

        private static void WriteUInt16(byte[] bytes, int offset, ushort value)
        {
            bytes[offset] = (byte)(value >> 8);
            bytes[offset + 1] = (byte)value;
        }

        public void Dispose()
        {
            _fallbackTypeface.Dispose();
        }

        private sealed class SystemFontFallbackTypeface : IPlatformTypeface
        {
            private readonly IConsoleTypeface _consoleTypeface;
            private readonly byte[] _fontData;
            private readonly Dictionary<string, ReadOnlyMemory<byte>> _tables;

            private SystemFontFallbackTypeface(IConsoleTypeface consoleTypeface, string fontPath)
            {
                _consoleTypeface = consoleTypeface;
                _fontData = File.ReadAllBytes(fontPath);
                _tables = ReadTables(_fontData);
            }

            public string FamilyName => _consoleTypeface.FamilyName;

            public FontWeight Weight => _consoleTypeface.Weight;

            public FontStyle Style => _consoleTypeface.Style;

            public FontStretch Stretch => _consoleTypeface.Stretch;

            public FontSimulations FontSimulations => _consoleTypeface.FontSimulations;

            public static IPlatformTypeface Create(IConsoleTypeface consoleTypeface)
            {
                var attemptedPaths = new List<string>();
                var failedCandidates = new List<string>();

                foreach (string fontPath in GetCandidateFontPaths())
                {
                    attemptedPaths.Add(fontPath);

                    if (!File.Exists(fontPath))
                        continue;

                    try
                    {
                        return new SystemFontFallbackTypeface(consoleTypeface, fontPath);
                    }
                    catch (IOException exception)
                    {
                        failedCandidates.Add($"{fontPath}: {exception.Message}");
                    }
                    catch (UnauthorizedAccessException exception)
                    {
                        failedCandidates.Add($"{fontPath}: {exception.Message}");
                    }
                    catch (InvalidOperationException exception)
                    {
                        failedCandidates.Add($"{fontPath}: {exception.Message}");
                    }
                }

                string attemptedPathText = string.Join(", ", attemptedPaths);
                string failedCandidateText = failedCandidates.Count == 0
                    ? string.Empty
                    : $" Failed candidates: {string.Join("; ", failedCandidates)}";
                Debug.WriteLine(
                    $"Unable to locate a system font for Avalonia 12 glyph typefaces. Tried: {attemptedPathText}.{failedCandidateText}");
                return new ConsoleOnlyFallbackTypeface(consoleTypeface);
            }

            public bool TryGetStream(out Stream stream)
            {
                stream = new MemoryStream(_fontData, false);
                return true;
            }

            public bool TryGetTable(OpenTypeTag tag, out ReadOnlyMemory<byte> table)
            {
                return _tables.TryGetValue(tag.ToString(), out table);
            }

            public void Dispose()
            {
            }

            private sealed class ConsoleOnlyFallbackTypeface : IPlatformTypeface
            {
                private readonly IConsoleTypeface _consoleTypeface;

                public ConsoleOnlyFallbackTypeface(IConsoleTypeface consoleTypeface)
                {
                    _consoleTypeface = consoleTypeface;
                }

                public string FamilyName => _consoleTypeface.FamilyName;

                public FontWeight Weight => _consoleTypeface.Weight;

                public FontStyle Style => _consoleTypeface.Style;

                public FontStretch Stretch => _consoleTypeface.Stretch;

                public FontSimulations FontSimulations => _consoleTypeface.FontSimulations;

                public bool TryGetStream(out Stream stream)
                {
                    stream = null;
                    return false;
                }

                public bool TryGetTable(OpenTypeTag tag, out ReadOnlyMemory<byte> table)
                {
                    table = default;
                    return false;
                }

                public void Dispose()
                {
                }
            }

            private static IEnumerable<string> GetCandidateFontPaths()
            {
                string windows = Environment.GetEnvironmentVariable("WINDIR");
                if (!string.IsNullOrWhiteSpace(windows))
                {
                    string fonts = Path.Combine(windows, "Fonts");
                    yield return Path.Combine(fonts, "segoeui.ttf");
                    yield return Path.Combine(fonts, "arial.ttf");
                    yield return Path.Combine(fonts, "consola.ttf");
                }

                yield return "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf";
                yield return "/usr/share/fonts/truetype/liberation2/LiberationSans-Regular.ttf";
                yield return "/usr/share/fonts/truetype/freefont/FreeSans.ttf";
                yield return "/System/Library/Fonts/Supplemental/Arial.ttf";
                yield return "/System/Library/Fonts/Supplemental/Helvetica.ttf";
                yield return "/System/Library/Fonts/SFNS.ttf";
            }

            private static Dictionary<string, ReadOnlyMemory<byte>> ReadTables(byte[] data)
            {
                int fontOffset = GetFontOffset(data);
                if (fontOffset < 0 ||
                    fontOffset + 12 > data.Length)
                    throw new InvalidOperationException("Invalid font table directory.");

                ushort tableCount = ReadUInt16(data, fontOffset + 4);
                var tables = new Dictionary<string, ReadOnlyMemory<byte>>(tableCount, StringComparer.OrdinalIgnoreCase);
                int tableRecordOffset = fontOffset + 12;

                for (int i = 0; i < tableCount; i++)
                {
                    int offset = tableRecordOffset + i * 16;
                    if (offset + 16 > data.Length)
                        throw new InvalidOperationException("Invalid font table record.");

                    string tag = Encoding.ASCII.GetString(data, offset, 4);
                    uint tableOffset = ReadUInt32(data, offset + 8);
                    uint tableLength = ReadUInt32(data, offset + 12);

                    if (tableOffset > int.MaxValue ||
                        tableLength > int.MaxValue)
                        continue;

                    int start = (int)tableOffset;
                    int length = (int)tableLength;
                    if (start > data.Length ||
                        length > data.Length - start)
                        continue;

                    tables[tag] = new ReadOnlyMemory<byte>(data, start, length);
                }

                return tables;
            }

            private static int GetFontOffset(byte[] data)
            {
                if (data.Length >= 16 &&
                    data[0] == (byte)'t' &&
                    data[1] == (byte)'t' &&
                    data[2] == (byte)'c' &&
                    data[3] == (byte)'f')
                {
                    uint offset = ReadUInt32(data, 12);
                    if (offset > int.MaxValue)
                        throw new InvalidOperationException("Invalid TrueType collection offset.");

                    return (int)offset;
                }

                return 0;
            }

            private static ushort ReadUInt16(byte[] data, int offset)
            {
                return (ushort)((data[offset] << 8) | data[offset + 1]);
            }

            private static uint ReadUInt32(byte[] data, int offset)
            {
                return ((uint)data[offset] << 24) |
                       ((uint)data[offset + 1] << 16) |
                       ((uint)data[offset + 2] << 8) |
                       data[offset + 3];
            }
        }
    }
}
