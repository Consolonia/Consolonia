using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Avalonia;
using Avalonia.Media;
using Consolonia.Core.Drawing.PixelBufferImplementation;
using Consolonia.Core.Helpers.InputProcessing;

namespace Consolonia.Core.Drawing
{
    internal class AnsiParser
    {
        private int _cursorX = 0;
        private int _cursorY = 0;
        private Color _foregroundColor = Colors.Gray;
        private Color _backgroundColor = Colors.Black;
        private int _savedCursorX = 0;
        private int _savedCursorY = 0;
        private int _sauceWidth = 0;
        private int _sauceHeight = 0;
        private FontWeight _weight = FontWeight.Normal;
        private FontStyle _style = FontStyle.Normal;
        
        private readonly List<List<Pixel>> _lines = new();

        private AnsiParser()
        {
            _lines.Add(new List<Pixel>());
        }

        public static PixelBuffer Parse(Stream stream)
        {
            var ms = new MemoryStream();
            stream.CopyTo(ms);
            ms.Position = 0;

            var sauce = ReadSauce(ms);
            ms.Position = 0;

            var parser = new AnsiParser();
            if (sauce != null)
            {
                parser._sauceWidth = sauce.Value.Width;
                parser._sauceHeight = sauce.Value.Height;
            }

            // ANSI art often uses CP437
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            using var reader = new StreamReader(ms, Encoding.GetEncoding(437));

            var matchers = new List<IMatcher<char>>();
            
            // Single CSI matcher for all escape sequences
            matchers.Add(new CsiMatcher((cmd, paramsStr) => parser.HandleCsi(cmd, paramsStr)));

            // G0/G1 character sets - single matcher
            matchers.Add(new EscTwoCharMatcher());

            // Generic fallback for any other character
            matchers.Add(new GenericMatcher<char>(c => parser.HandleChar(c), c => c != '\x1B'));

            var processor = new InputProcessor<char>(matchers);

            var content = reader.ReadToEnd();
            int eofIndex = content.IndexOf('\x1A');
            if (eofIndex >= 0) content = content.Substring(0, eofIndex);
            
            processor.ProcessChunk(content.ToCharArray());
            
            return parser.ToPixelBuffer();
        }

        private struct SauceInfo
        {
            public ushort Width;
            public ushort Height;
        }

        private static SauceInfo? ReadSauce(Stream stream)
        {
            if (stream.Length < 128) return null;
            long originalPos = stream.Position;
            try
            {
                stream.Seek(-128, SeekOrigin.End);
                byte[] buffer = new byte[128];
                int read = stream.Read(buffer, 0, 128);
                if (read < 128) return null;

                if (Encoding.ASCII.GetString(buffer, 0, 5) == "SAUCE")
                {
                    // DataType 1 is Character (ANSI)
                    if (buffer[94] != 1) return null;

                    ushort width = (ushort)(buffer[96] | (buffer[97] << 8));
                    ushort height = (ushort)(buffer[98] | (buffer[99] << 8));
                    return new SauceInfo { Width = width, Height = height };
                }
            }
            catch (IOException)
            {
                return null;
            }
            finally
            {
                stream.Seek(originalPos, SeekOrigin.Begin);
            }
            return null;
        }

        /// <summary>
        /// Matches any CSI sequence: ESC [ params command
        /// where params are digits/semicolons/question marks and command is a single letter.
        /// </summary>
        private class CsiMatcher : IMatcher<char>
        {
            private readonly StringBuilder _accumulator = new();
            private readonly Action<char, string> _onComplete;

            public CsiMatcher(Action<char, string> onComplete) => _onComplete = onComplete;

            public AppendResult Append(char input)
            {
                int len = _accumulator.Length;

                if (len == 0 && input == '\x1B')
                {
                    _accumulator.Append(input);
                    return AppendResult.Match;
                }

                if (len == 1 && input == '[')
                {
                    _accumulator.Append(input);
                    return AppendResult.Match;
                }

                if (len >= 2)
                {
                    if (input >= 0x40 && input <= 0x7E) // final byte (command letter)
                    {
                        string paramsStr = _accumulator.ToString(2, len - 2);
                        _onComplete(input, paramsStr);
                        _accumulator.Clear();
                        return AppendResult.AutoFlushed;
                    }

                    if ((input >= '0' && input <= '9') || input == ';' || input == '?')
                    {
                        _accumulator.Append(input);
                        return AppendResult.Match;
                    }
                }

                _accumulator.Clear();
                return AppendResult.NoMatch;
            }

            public bool TryFlush() => false;
            public void Reset() => _accumulator.Clear();
            public string GetDebugInfo() => $"CsiMatcher {{{(_accumulator.Length == 0 ? "_" : _accumulator.ToString())}}}";
        }

        /// <summary>
        /// Matches ESC followed by a single non-[ character and then one more character (e.g., ESC ( B for G0 charset).
        /// These sequences are silently ignored.
        /// </summary>
        private class EscTwoCharMatcher : IMatcher<char>
        {
            private int _state; // 0=idle, 1=got ESC, 2=got ESC+char

            public AppendResult Append(char input)
            {
                switch (_state)
                {
                    case 0 when input == '\x1B':
                        _state = 1;
                        return AppendResult.Match;
                    case 1 when input != '[' && input != '\x1B':
                        _state = 2;
                        return AppendResult.Match;
                    case 2:
                        _state = 0;
                        return AppendResult.AutoFlushed; // consume and ignore the 3-char sequence
                    default:
                        _state = 0;
                        return AppendResult.NoMatch;
                }
            }

            public bool TryFlush() => false;
            public void Reset() => _state = 0;
            public string GetDebugInfo() => $"EscTwoCharMatcher state={_state}";
        }

        private void HandleCsi(char command, string paramsStr)
        {
            switch (command)
            {
                case 'm': HandleSgr(paramsStr); break;
                case 'H':
                case 'f': HandleCursorPosition(paramsStr); break;
                case 'A': HandleCursorMove(0, -ParseInt(paramsStr, 1)); break;
                case 'B': HandleCursorMove(0, ParseInt(paramsStr, 1)); break;
                case 'C': HandleCursorMove(ParseInt(paramsStr, 1), 0); break;
                case 'D': HandleCursorMove(-ParseInt(paramsStr, 1), 0); break;
                case 'F': HandleCursorMove(0, -ParseInt(paramsStr, 1)); _cursorX = 0; break;
                case 'G': _cursorX = Math.Max(0, ParseInt(paramsStr, 1) - 1); break;
                case 'J': HandleErase(paramsStr); break;
                case 'K': HandleEraseLine(paramsStr); break;
                case 's': _savedCursorX = _cursorX; _savedCursorY = _cursorY; break;
                case 'u': _cursorX = _savedCursorX; _cursorY = _savedCursorY; break;
            }
        }

        private int ParseInt(string s, int defaultValue)
        {
            if (string.IsNullOrEmpty(s)) return defaultValue;
            if (int.TryParse(s, out int result)) return result;
            return defaultValue;
        }

        private void HandleChar(char c)
        {
            if (c == '\r') { _cursorX = 0; return; }
            if (c == '\n') { _cursorY++; _cursorX = 0; return; }
            if (c == '\x1B') return;

            int wrapWidth = _sauceWidth > 0 ? _sauceWidth : 80;
            if (_cursorX >= wrapWidth)
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
                _lines.Add(new List<Pixel>());
        }

        private void EnsureColumn(int y, int x)
        {
            while (_lines[y].Count <= x)
                _lines[y].Add(new Pixel(new PixelBackground(_backgroundColor)));
        }

        private void HandleSgr(string paramsStr)
        {
             if (string.IsNullOrEmpty(paramsStr))
             {
                 ResetStyle();
                 return;
             }
             var parts = paramsStr.Split(';');
             for (int i = 0; i < parts.Length; i++)
             {
                 string part = parts[i];
                 if (string.IsNullOrEmpty(part) || part == "0") ResetStyle();
                 else if (part == "1") _weight = FontWeight.Bold;
                 else if (part == "2") _weight = FontWeight.Light;
                 else if (part == "3") _style = FontStyle.Italic;
                 else if (int.TryParse(part, out int code))
                 {
                     if (code >= 30 && code <= 37) _foregroundColor = AnsiColors[code - 30];
                     else if (code >= 40 && code <= 47) _backgroundColor = AnsiColors[code - 40];
                     else if (code >= 90 && code <= 97) _foregroundColor = AnsiColors[code - 90 + 8];
                     else if (code >= 100 && code <= 107) _backgroundColor = AnsiColors[code - 100 + 8];
                     else if (code == 39) _foregroundColor = Colors.Gray;
                     else if (code == 49) _backgroundColor = Colors.Black;
                }
            }
        }

        private void ResetStyle()
        {
            _foregroundColor = Colors.Gray;
            _backgroundColor = Colors.Black;
            _weight = FontWeight.Normal;
            _style = FontStyle.Normal;
        }

        private void HandleCursorPosition(string paramsStr)
        {
            var parts = paramsStr.Split(';');
            int y = 1;
            int x = 1;
            if (parts.Length > 0 && int.TryParse(parts[0], out int py)) y = py;
            if (parts.Length > 1 && int.TryParse(parts[1], out int px)) x = px;
            
            _cursorX = Math.Max(0, x - 1);
            _cursorY = Math.Max(0, y - 1);
            EnsureLine(_cursorY);
        }

        private void HandleCursorMove(int dx, int dy)
        {
            _cursorX = Math.Max(0, _cursorX + dx);
            _cursorY = Math.Max(0, _cursorY + dy);
            EnsureLine(_cursorY);
        }
        
        private void HandleErase(string paramsStr)
        {
            if (paramsStr == "2")
            {
                _lines.Clear();
                _lines.Add(new List<Pixel>());
                _cursorX = 0;
                _cursorY = 0;
            }
        }

        private void HandleEraseLine(string paramsStr)
        {
            int mode = ParseInt(paramsStr, 0);
            EnsureLine(_cursorY);
            var line = _lines[_cursorY];
            
            if (mode == 0) // From cursor to end of line
            {
                // We don't really have a fixed width line, so we just clear what's there
                if (_cursorX < line.Count)
                    line.RemoveRange(_cursorX, line.Count - _cursorX);
            }
            else if (mode == 1) // From beginning to cursor
            {
                for (int i = 0; i <= Math.Min(_cursorX, line.Count - 1); i++)
                    line[i] = new Pixel(new PixelBackground(_backgroundColor));
            }
            else if (mode == 2) // Entire line
            {
                line.Clear();
            }
        }

        private PixelBuffer ToPixelBuffer()
        {
            int height = Math.Max(_sauceHeight, _lines.Count);
            int width = Math.Max(_sauceWidth, _lines.Count > 0 ? _lines.Max(l => l.Count) : 0);
            if (width == 0 || height == 0) return new PixelBuffer(1, 1);

            var pixelBuffer = new PixelBuffer((ushort)width, (ushort)height);
            for (ushort y = 0; y < (ushort)height; y++)
            {
                for (ushort x = 0; x < (ushort)width; x++)
                {
                    if (y < _lines.Count && x < _lines[y].Count)
                        pixelBuffer[x, y] = _lines[y][x];
                    else
                        pixelBuffer[x, y] = new Pixel(new PixelBackground(Colors.Black));
                }
            }
            return pixelBuffer;
        }

        private static readonly Color[] AnsiColors = new Color[]
        {
            Color.FromRgb(0, 0, 0),       // Black
            Color.FromRgb(128, 0, 0),     // DarkRed
            Color.FromRgb(0, 128, 0),     // DarkGreen
            Color.FromRgb(128, 128, 0),   // DarkYellow
            Color.FromRgb(0, 0, 128),     // DarkBlue
            Color.FromRgb(128, 0, 128),   // DarkMagenta
            Color.FromRgb(0, 128, 128),   // DarkCyan
            Color.FromRgb(192, 192, 192), // Gray
            Color.FromRgb(128, 128, 128), // DarkGray
            Color.FromRgb(255, 0, 0),     // Red
            Color.FromRgb(0, 255, 0),     // Green
            Color.FromRgb(255, 255, 0),   // Yellow
            Color.FromRgb(0, 0, 255),     // Blue
            Color.FromRgb(255, 0, 255),   // Magenta
            Color.FromRgb(0, 255, 255),   // Cyan
            Color.FromRgb(255, 255, 255)  // White
        };
    }
}
