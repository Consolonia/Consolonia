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
        private FontWeight _weight = FontWeight.Normal;
        private FontStyle _style = FontStyle.Normal;
        
        private readonly List<List<Pixel>> _lines = new();

        private AnsiParser()
        {
            _lines.Add(new List<Pixel>());
        }

        public static PixelBuffer Parse(Stream stream)
        {
            var parser = new AnsiParser();
            
            // ANSI art often uses CP437
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            using var reader = new StreamReader(stream, Encoding.GetEncoding(437));

            var matchers = new List<IMatcher<char>>();
            
            // CSI sequences
            // Handle common CSI commands used in ANSI art
            string csiCommands = "mABCDFGHJKfsuhl";
            foreach (char command in csiCommands)
            {
                char cmd = command;
                matchers.Add(new AnsiSequenceMatcher(paramsStr => 
                    parser.HandleCsi(cmd, paramsStr), 
                    "\x1B[", 
                    cmd.ToString()));
            }

            // G0/G1 character sets
            matchers.Add(new AnsiSequenceMatcher(_ => { }, "\x1B(", "B"));
            matchers.Add(new AnsiSequenceMatcher(_ => { }, "\x1B)", "0"));

            // Generic fallback for any other character
            matchers.Add(new GenericMatcher<char>(c => parser.HandleChar(c), c => c != '\x1B'));

            var processor = new InputProcessor<char>(matchers);

            var content = reader.ReadToEnd();
            processor.ProcessChunk(content.ToCharArray());
            
            return parser.ToPixelBuffer();
        }

        private class AnsiSequenceMatcher(Action<string> onComplete, string startsWith, string endsWith)
            : StartsEndsWithMatcher<char>(onComplete, c => new Rune(c), startsWith, endsWith)
        {
            public override bool TryFlush() => false;
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
            int height = _lines.Count;
            int width = _lines.Count > 0 ? _lines.Max(l => l.Count) : 0;
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
