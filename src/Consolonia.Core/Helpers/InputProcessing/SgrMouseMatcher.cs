using System;
using System.Text;
using System.Text.RegularExpressions;

namespace Consolonia.Core.Helpers.InputProcessing
{
    /// <summary>
    ///     Matches SGR extended mouse tracking sequences.
    ///     Format: CSI &lt; button ; x ; y M  (press/motion)
    ///     Format: CSI &lt; button ; x ; y m  (release)
    /// </summary>
    public class SgrMouseMatcher<T>(
        Action<(int button, int x, int y, bool isRelease)> onComplete,
        Func<T, Rune> toRune)
        : MatcherWithComplete<T, (int button, int x, int y, bool isRelease)>(onComplete)
    {
        private readonly StringBuilder _accumulator = new();

        // Matches: ESC [ < button ; x ; y M/m
        private static readonly Regex CompletePattern = new(@"^\x1B\[<(\d+);(\d+);(\d+)([Mm])$");

        public override AppendResult Append(T input)
        {
            Rune rune = toRune(input);
            _accumulator.Append(rune);

            string current = _accumulator.ToString();

            // Check if it's a complete SGR mouse sequence
            Match match = CompletePattern.Match(current);
            if (match.Success)
            {
                int button = int.Parse(match.Groups[1].Value);
                int x = int.Parse(match.Groups[2].Value);
                int y = int.Parse(match.Groups[3].Value);
                bool isRelease = match.Groups[4].Value == "m";

                Complete((button, x, y, isRelease));
                _accumulator.Clear();
                return AppendResult.AutoFlushed;
            }

            // Check if it's a valid prefix
            if (IsValidPrefix(current))
                return AppendResult.Match;

            // Not a match
            _accumulator.Length--;
            if (_accumulator.Length > 0)
                _accumulator.Clear();

            return AppendResult.NoMatch;
        }

        private static bool IsValidPrefix(string s)
        {
            // Valid prefixes: \x1B, \x1B[, \x1B[<, \x1B[<digits, \x1B[<digits;, \x1B[<digits;digits,
            // \x1B[<digits;digits;, \x1B[<digits;digits;digits
            if (s.Length == 0) return false;
            if (s[0] != '\x1B') return false;
            if (s.Length == 1) return true;
            if (s[1] != '[') return false;
            if (s.Length == 2) return true;
            if (s[2] != '<') return false;
            if (s.Length == 3) return true;

            int i = 3;
            // Three groups of digits separated by ;
            for (int group = 0; group < 3; group++)
            {
                if (i >= s.Length) return true;
                if (!char.IsAsciiDigit(s[i])) return false;
                while (i < s.Length && char.IsAsciiDigit(s[i])) i++;
                if (i >= s.Length) return true;

                if (group < 2)
                {
                    if (s[i] != ';') return false;
                    i++;
                }
                else
                {
                    // After last digit group, expect M or m
                    return (s[i] == 'M' || s[i] == 'm') && i == s.Length - 1;
                }
            }

            return true;
        }

        public override bool TryFlush()
        {
            return _accumulator.Length != 0;
        }

        public override void Reset()
        {
            // Intentionally not clearing - same pattern as StartsEndsWithMatcher
        }

        public override string GetDebugInfo()
        {
            return $"{GetType().Name} {{{(_accumulator.Length == 0 ? "_" : _accumulator.ToString())}}}";
        }
    }
}
