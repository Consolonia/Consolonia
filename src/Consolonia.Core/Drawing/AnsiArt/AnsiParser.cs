using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Avalonia.Media;
using Consolonia.Core.Drawing.PixelBufferImplementation;
using Consolonia.Core.Helpers.InputProcessing;

namespace Consolonia.Core.Drawing.AnsiArt
{
    internal partial class AnsiParser
    {
        private static readonly Color[] AnsiColors =
        [
            Color.FromRgb(0, 0, 0), // Black
            Color.FromRgb(128, 0, 0), // DarkRed
            Color.FromRgb(0, 128, 0), // DarkGreen
            Color.FromRgb(128, 128, 0), // DarkYellow
            Color.FromRgb(0, 0, 128), // DarkBlue
            Color.FromRgb(128, 0, 128), // DarkMagenta
            Color.FromRgb(0, 128, 128), // DarkCyan
            Color.FromRgb(192, 192, 192), // Gray
            Color.FromRgb(128, 128, 128), // DarkGray
            Color.FromRgb(255, 0, 0), // Red
            Color.FromRgb(0, 255, 0), // Green
            Color.FromRgb(255, 255, 0), // Yellow
            Color.FromRgb(0, 0, 255), // Blue
            Color.FromRgb(255, 0, 255), // Magenta
            Color.FromRgb(0, 255, 255), // Cyan
            Color.FromRgb(255, 255, 255) // White
        ];

        private static readonly Color DefaultBackgroundColor = Colors.Black;
        private static readonly Color DefaultForegroundColor = Colors.White;

        private readonly List<List<Pixel>> _lines = [];
        private Color _backgroundColor = DefaultBackgroundColor;
        private int _cursorX;
        private int _cursorY;
        private Color _foregroundColor = DefaultForegroundColor;
        private int _sauceHeight;
        private int _sauceWidth;
        private int _savedCursorX;
        private int _savedCursorY;
        private FontStyle _style = FontStyle.Normal;
        private FontWeight _weight = FontWeight.Normal;
        private int _wrapWidth = 80;

        static AnsiParser()
        {
            // ANSI art often uses CP437
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        }

        private AnsiParser()
        {
            _lines.Add([]);
        }

        public static PixelBuffer Parse(Stream stream)
        {
            SauceInfo? sauce = null;

            bool canSeek = stream.CanSeek;
            long position = -1;
            if (canSeek)
            {
                position = stream.Position;
                sauce = ReadSauce(stream);
            }

            var parser = new AnsiParser();
            if (sauce != null)
            {
                parser._sauceWidth = sauce.Value.Width;
                parser._sauceHeight = sauce.Value.Height;
                parser._wrapWidth = sauce.Value.Width;
            }

            if (canSeek)
                stream.Seek(position, SeekOrigin.Begin);

            using var reader = new StreamReader(stream, Encoding.GetEncoding(437));

            bool sauceStarted = false;

            var matchers = new List<IMatcher<char>>
            {
                // Single CSI matcher for all escape sequences
                new CsiMatcher((cmd, paramsStr) =>
                {
                    if (!sauceStarted)
                        parser.HandleCsi(cmd, paramsStr);
                }),

                // PCBoard / BBS viewer directives: `@NOPAUSE@`, `@PAUSE@`, `@MUSIC:…@`, etc.
                new RegexMatcher<char>(_ =>
                {
                    /* discard */
                }, c => new Rune(c), "@[^@\u001b\r\n]+@"),

                // Generic fallback for any other character or SAUSE
                new RegexMatcher<char>(tuple =>
                {
                    foreach (char c in tuple.Item2)
                        if (!sauceStarted)
                        {
                            // if end of file
                            if (c == 0x1A)
                            {
                                sauceStarted = true;
                                continue;
                            }

                            parser.HandleChar(c);
                        }
                }, c => new Rune(c), "[^\x1B]")
            };

            var processor = new InputProcessor<char>(matchers);

            while (!reader.EndOfStream)
            {
                string line = reader.ReadLine() + Environment.NewLine;
                if (reader.EndOfStream)
                    line = line.TrimEnd();
                processor.ProcessChunk(line.ToCharArray());
            }

            return parser.ToPixelBuffer();
        }

        private static SauceInfo? ReadSauce(Stream stream)
        {
            if (stream.Length < 128) return null;
            stream.Seek(-128, SeekOrigin.End);
            byte[] buffer = new byte[128];
            int read = stream.Read(buffer, 0, 128);
            if (read < 128)
                throw new InvalidOperationException("Could not read the SAUCE end");

            if (Encoding.ASCII.GetString(buffer, 0, 5) == "SAUCE")
            {
                // DataType 1 is Character (ANSI)
                if (buffer[94] != 1) return null;

                ushort width = (ushort)(buffer[96] | (buffer[97] << 8));
                ushort height = (ushort)(buffer[98] | (buffer[99] << 8));
                return new SauceInfo { Width = width, Height = height };
            }

            return null;
        }

        private void HandleCsi(char command, string paramsStr)
        {
            //todo: this default values are by Claude. Even not sure why they required
            switch (command)
            {
                case 'm': HandleSgr(paramsStr); break;
                case 'H':
                case 'f': HandleCursorPosition(paramsStr); break;
                case 'A': HandleCursorMove(0, -ParseInt(paramsStr, 1)); break;
                case 'B': HandleCursorMove(0, ParseInt(paramsStr, 1)); break;
                case 'C': HandleCursorMove(ParseInt(paramsStr, 1), 0); break;
                case 'D': HandleCursorMove(-ParseInt(paramsStr, 1), 0); break;
                case 'F':
                    HandleCursorMove(0, -ParseInt(paramsStr, 1));
                    _cursorX = 0;
                    break;
                case 'G': _cursorX = Math.Max(0, ParseInt(paramsStr, 1) - 1); break;
                case 'J': HandleErase(paramsStr); break;
                case 'K': HandleEraseLine(paramsStr); break;
                case 's':
                    _savedCursorX = _cursorX;
                    _savedCursorY = _cursorY;
                    break;
                case 'u':
                    _cursorX = _savedCursorX;
                    _cursorY = _savedCursorY;
                    break;
            }
        }

        private static int ParseInt(string s, int defaultValue)
        {
            return string.IsNullOrEmpty(s) ? defaultValue : int.Parse(s);
        }

        private void HandleChar(char c)
        {
            switch (c)
            {
                case '\r':
                    _cursorX = 0;
                    return;
                case '\n':
                    _cursorY++;
                    _cursorX = 0;
                    return;
            }

            if (_wrapWidth > 0 && _cursorX >= _wrapWidth)
            {
                _cursorX = 0;
                _cursorY++;
            }

            EnsureLine(_cursorY);
            EnsureColumn(_cursorY, _cursorX);

            _lines[_cursorY][_cursorX] = new Pixel(
                new PixelForeground(new Symbol(c), _foregroundColor, _weight, _style),
                new PixelBackground(_backgroundColor)
            );
            _cursorX++;
        }

        private void EnsureLine(int y)
        {
            while (_lines.Count <= y)
                _lines.Add([]);
        }

        private void EnsureColumn(int y, int x)
        {
            while (_lines[y].Count <= x)
                _lines[y].Add(new Pixel(new PixelBackground(DefaultBackgroundColor)));
        }

        private void HandleSgr(string paramsStr)
        {
            if (string.IsNullOrEmpty(paramsStr))
            {
                ResetStyle();
                return;
            }

            string[] parts = paramsStr.Split(';');
            foreach (string part in parts)
                if (string.IsNullOrEmpty(part) || part == "0")
                    ResetStyle();
                else
                    switch (part)
                    {
                        case "1":
                            _weight = FontWeight.Bold;
                            break;
                        case "2":
                            _weight = FontWeight.Light;
                            break;
                        case "3":
                            _style = FontStyle.Italic;
                            break;
                        default:
                        {
                            if (int.TryParse(part, out int code))
                                switch (code)
                                {
                                    case >= 30 and <= 37:
                                        _foregroundColor = AnsiColors[code - 30];
                                        break;
                                    case >= 40 and <= 47:
                                        _backgroundColor = AnsiColors[code - 40];
                                        break;
                                    case >= 90 and <= 97:
                                        _foregroundColor = AnsiColors[code - 90 + 8];
                                        break;
                                    case >= 100 and <= 107:
                                        _backgroundColor = AnsiColors[code - 100 + 8];
                                        break;
                                    case 39:
                                        _foregroundColor = DefaultForegroundColor;
                                        break;
                                    case 49:
                                        _backgroundColor = DefaultBackgroundColor;
                                        break;
                                }

                            break;
                        }
                    }
        }

        private void ResetStyle()
        {
            _foregroundColor = DefaultForegroundColor;
            _backgroundColor = DefaultBackgroundColor;
            _weight = FontWeight.Normal;
            _style = FontStyle.Normal;
        }

        private void HandleCursorPosition(string paramsStr)
        {
            //todo: these 1 and 0 defaults are by Claude
            string[] parts = paramsStr.Split(';');
            int y = 1;
            int x = 1;

            if (parts.Length > 0)
            {
                y = 0;

                if (parts[0].Length > 0)
                    y = int.Parse(parts[0]);
            }

            if (parts.Length > 1) x = int.Parse(parts[1]);

            _cursorX = Math.Max(0, x - 1);
            _cursorY = Math.Max(0, y - 1);
            EnsureLine(_cursorY);
            EnsureColumn(_cursorY, _cursorX);
        }

        private void HandleCursorMove(int dx, int dy)
        {
            // todo: max iss by Claude
            _cursorX = Math.Max(0, _cursorX + dx);
            _cursorY = Math.Max(0, _cursorY + dy);
            EnsureLine(_cursorY);
        }

        private void HandleErase(string paramsStr)
        {
            if (paramsStr == "2")
            {
                _lines.Clear();
                _lines.Add([]);
                _cursorX = 0;
                _cursorY = 0;
            }
        }

        private void HandleEraseLine(string paramsStr)
        {
            int mode = ParseInt(paramsStr, 0);
            EnsureLine(_cursorY);

            List<Pixel> line = _lines[_cursorY];

            switch (mode)
            {
                // From cursor to end of line
                case 0:
                {
                    // We don't really have a fixed width line, so we just clear what's there
                    if (_cursorX < line.Count)
                        line.RemoveRange(_cursorX, line.Count - _cursorX);
                    break;
                }
                // From beginning to cursor
                case 1:
                {
                    for (int i = 0; i <= Math.Min(_cursorX, line.Count - 1); i++)
                        line[i] = new Pixel(new PixelBackground(_backgroundColor));
                    break;
                }
                // Entire line
                case 2:
                    line.Clear();
                    break;
            }
        }

        private PixelBuffer ToPixelBuffer()
        {
            int height = Math.Max(_sauceHeight, _lines.Count);
            int width = Math.Max(_sauceWidth, _lines.Count > 0 ? _lines.Max(l => l.Count) : 0);
            if (width == 0 || height == 0)
                return new PixelBuffer(1, 1); //todo: this 1,1 by Claude, why?

            var defaultBackgroundPixel = new Pixel(new PixelBackground(DefaultBackgroundColor));
            var pixelBuffer = new PixelBuffer((ushort)width, (ushort)height);

            for (ushort y = 0; y < (ushort)height; y++)
            for (ushort x = 0; x < (ushort)width; x++)
                pixelBuffer[x, y] = y < _lines.Count && x < _lines[y].Count
                    ? _lines[y][x]
                    : defaultBackgroundPixel;

            return pixelBuffer;
        }

        private struct SauceInfo
        {
            public ushort Width;
            public ushort Height;
        }
    }
}