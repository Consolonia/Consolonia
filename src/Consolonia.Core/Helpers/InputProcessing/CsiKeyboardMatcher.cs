using System;
using System.Text;
using System.Text.RegularExpressions;

namespace Consolonia.Core.Helpers.InputProcessing
{
    /// <summary>
    ///     Matches Kitty keyboard protocol CSI u sequences and legacy CSI functional key sequences.
    ///     Formats:
    ///       CSI u:      ESC [ keycode ; modifiers u
    ///       CSI tilde:  ESC [ number ; modifiers ~     (Insert, Delete, PgUp, PgDn, F5-F12)
    ///       CSI letter: ESC [ 1 ; modifiers letter     (Arrows, Home, End, F1-F4)
    ///       CSI letter: ESC [ letter                    (unmodified arrows, Home, End, F1-F4)
    /// </summary>
    public class CsiKeyboardMatcher<T>(
        Action<(int keyCode, int modifiers, int eventType, char terminator)> onComplete,
        Func<T, Rune> toRune)
        : MatcherWithComplete<T, (int keyCode, int modifiers, int eventType, char terminator)>(onComplete)
    {
        private readonly StringBuilder _accumulator = new();

        // Matches all CSI keyboard formats:
        //   ESC [ number ; modifiers : eventtype u/~
        //   ESC [ number ; modifiers u/~
        //   ESC [ number u/~
        //   ESC [ 1 ; modifiers : eventtype letter
        //   ESC [ 1 ; modifiers letter
        //   ESC [ letter  (bare functional key, e.g. ESC[A for Up arrow)
        // Valid terminator letters: A-D (arrows), F/H (End/Home), P-S (F1-F4)
        private static readonly Regex CompletePattern =
            new(@"^\x1B\[(\d+)?(;(\d+)(:(\d+))?)?([A-DFHPQRSu~])$");

        /// <summary>
        ///     Valid terminator characters for CSI functional key sequences.
        /// </summary>
        private const string ValidTerminators = "ABCDFHPQRSu~";

        public override AppendResult Append(T input)
        {
            Rune rune = toRune(input);
            _accumulator.Append(rune);

            string current = _accumulator.ToString();

            // Check if it's a complete CSI sequence
            Match match = CompletePattern.Match(current);
            if (match.Success)
            {
                int keyCode = match.Groups[1].Success ? int.Parse(match.Groups[1].Value) : 0;
                int modifiers = match.Groups[3].Success ? int.Parse(match.Groups[3].Value) : 1;
                int eventType = match.Groups[5].Success ? int.Parse(match.Groups[5].Value) : 1;
                char terminator = match.Groups[6].Value[0];

                Complete((keyCode, modifiers, eventType, terminator));
                _accumulator.Clear();
                return AppendResult.AutoFlushed;
            }

            // Check if it's a valid prefix of a CSI sequence
            if (IsValidPrefix(current))
                return AppendResult.Match;

            // Not a match, remove the last character
            _accumulator.Length--;
            if (_accumulator.Length > 0)
            {
                // We had accumulated some chars but this one broke the pattern
                _accumulator.Clear();
            }

            return AppendResult.NoMatch;
        }

        private static bool IsValidPrefix(string s)
        {
            if (s.Length == 0) return false;
            if (s[0] != '\x1B') return false;
            if (s.Length == 1) return true;
            if (s[1] != '[') return false;
            if (s.Length == 2) return true;

            int i = 2;

            // After ESC[, we can have:
            //   - A terminator letter directly (e.g., ESC[A) — but that's a complete match, not prefix
            //   - Digits followed by more content

            // If position 2 is a valid terminator letter, that would be a complete sequence, not a prefix
            // So for prefix checking, we only need to handle the digit case
            if (char.IsAsciiDigit(s[i]))
            {
                // Consume digits
                while (i < s.Length && char.IsAsciiDigit(s[i])) i++;
                if (i >= s.Length) return true;

                // After digits, expect ; or a terminator
                if (ValidTerminators.Contains(s[i])) return i == s.Length - 1; // complete, last char
                if (s[i] != ';') return false;
                i++;
                if (i >= s.Length) return true;

                // Expect digits for modifiers
                if (!char.IsAsciiDigit(s[i])) return false;
                while (i < s.Length && char.IsAsciiDigit(s[i])) i++;
                if (i >= s.Length) return true;

                // After modifiers, expect : or a terminator
                if (ValidTerminators.Contains(s[i])) return i == s.Length - 1;
                if (s[i] != ':') return false;
                i++;
                if (i >= s.Length) return true;

                // Expect digits for event type
                if (!char.IsAsciiDigit(s[i])) return false;
                while (i < s.Length && char.IsAsciiDigit(s[i])) i++;
                if (i >= s.Length) return true;

                // After event type, expect a terminator
                if (ValidTerminators.Contains(s[i])) return i == s.Length - 1;

                return false;
            }

            // Not a digit and not handled by CompletePattern (bare letter like ESC[A is complete, not prefix)
            return false;
        }

        public override bool TryFlush()
        {
            // CSI sequences are auto-flushed on completion, so we just indicate
            // we have pending data to prevent lower-priority matchers from flushing
            return _accumulator.Length != 0;
        }

        public override void Reset()
        {
            // Intentionally not clearing - same pattern as StartsEndsWithMatcher
            // to prevent false resets from lower-priority matchers
        }

        public override string GetDebugInfo()
        {
            return $"{GetType().Name} {{{(_accumulator.Length == 0 ? "_" : _accumulator.ToString())}}}";
        }
    }
}
