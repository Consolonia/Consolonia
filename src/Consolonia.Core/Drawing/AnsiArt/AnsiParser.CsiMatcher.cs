using System;
using System.Text;
using Consolonia.Core.Helpers.InputProcessing;

namespace Consolonia.Core.Drawing.AnsiArt
{
    internal partial class AnsiParser
    {
        /// <summary>
        /// Matches any CSI sequence: ESC [ params command
        /// Example: \x1B[48;5;21m
        /// </summary>
        private class CsiMatcher(Action<char, string> onComplete) : IMatcher<char>
        {
            private readonly StringBuilder _accumulator = new();

            public AppendResult Append(char input)
            {
                int len = _accumulator.Length;

                switch (len)
                {
                    case 0 when input == '\x1B':
                    case 1 when input == '[':
                        _accumulator.Append(input);
                        return AppendResult.Match;

                    // final char = command
                    case >= 2 when input >= 0x40 && input <= 0x7E:
                    {
                        string paramsStr = _accumulator.ToString(2, len - 2);
                        onComplete(input, paramsStr);
                        _accumulator.Clear();
                        return AppendResult.AutoFlushed;
                    }

                    // parameters
                    case >= 2 when input is >= '0' and <= '9' or ';' or '?':
                        _accumulator.Append(input);
                        return AppendResult.Match;

                    default:
                        _accumulator.Clear();
                        return AppendResult.NoMatch;
                }
            }

            public bool TryFlush() => false;
            public void Reset() => _accumulator.Clear();

            public string GetDebugInfo()
            {
                return $"{GetType().Name}  {{{(_accumulator.Length == 0 ? "_" : _accumulator.ToString())}}}";
            }
        }
    }
}