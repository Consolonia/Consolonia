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
    public partial class CsiKeyboardMatcher<T>(
        Action<(int keyCode, int modifiers, int eventType, char terminator)> onComplete,
        Func<T, Rune> toRune)
        : MatcherWithComplete<T, (int keyCode, int modifiers, int eventType, char terminator)>(onComplete)
    {
        private readonly StringBuilder _accumulator = new();

        public override AppendResult Append(T input)
        {
            Rune rune = toRune(input);
            _accumulator.Append(rune);

            string current = _accumulator.ToString();

            // Check if it's a complete CSI sequence
            Match match = GetCompletionPatternRegex().Match(current);
            if (match.Success)
            {
                int keyCode = match.Groups["keyCode"].Success ? int.Parse(match.Groups["keyCode"].Value) : 0;
                char terminator = match.Groups["terminator"].Value[0];

                // Don't match bracketed paste mode sequences, let PasteBlockMatcher handle them
                if (terminator == '~' && keyCode is 200 or 201)
                {
                    _accumulator.Clear();
                    return AppendResult.NoMatch;
                }

                int modifiers = match.Groups["modifiers"].Success ? int.Parse(match.Groups["modifiers"].Value) : 1;
                int eventType = match.Groups["eventType"].Success ? int.Parse(match.Groups["eventType"].Value) : 1;

                Complete((keyCode, modifiers, eventType, terminator));
                _accumulator.Clear();
                return AppendResult.AutoFlushed;
            }

            // Not a complete CSI sequence yet, but lets check if it's a valid prefix
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

        private static bool IsValidPrefix(string input)
        {
            if (input.Length == 0) return false;
            if (input[0] != '\x1B') return false;
            if (input.Length == 1) return true;
            if (input[1] != '[') return false;
            if (input.Length == 2) return true;

            int i = 2;

            // After ESC[, we can have:
            //   - A terminator letter directly (e.g., ESC[A) — but that's a complete match, not prefix
            //   - Digits followed by more content

            // If position 2 is a valid terminator letter, that would be a complete sequence, not a prefix
            // So for prefix checking, we only need to handle the digit case
            if (char.IsAsciiDigit(input[i]))
            {
                // Consume digits
                while (i < input.Length && char.IsAsciiDigit(input[i])) i++;
                if (i >= input.Length) return true;

                // After digits, expect ; or : or a terminator
                if (ValidCSITerminators.Contains(input[i])) return i == input.Length - 1; // complete, last char
                if (input[i] != ';' && input[i] != ':') return false;
                i++;
                if (i >= input.Length) return true;

                // Expect digits for modifiers
                if (!char.IsAsciiDigit(input[i])) return false;
                while (i < input.Length && char.IsAsciiDigit(input[i])) i++;
                if (i >= input.Length) return true;

                // After modifiers, expect : or ; or a terminator
                if (ValidCSITerminators.Contains(input[i])) return i == input.Length - 1;
                if (input[i] != ':' && input[i] != ';') return false;
                i++;
                if (i >= input.Length) return true;

                // Expect digits for event type
                if (!char.IsAsciiDigit(input[i])) return false;
                while (i < input.Length && char.IsAsciiDigit(input[i])) i++;
                if (i >= input.Length) return true;

                // After event type, expect a terminator
                if (ValidCSITerminators.Contains(input[i])) return i == input.Length - 1;

                return false;
            }

            // Not a digit and not handled by CompletePattern (bare letter like ESC[A is complete, not prefix)
            return false;
        }

        public override bool TryFlush()
        {
            // Always return false: CSI sequences are auto-flushed on completion,
            // and returning true here would block lower-priority matchers (like
            // SgrMouseMatcher) from flushing when this matcher has partial data.
            return false;
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

        /// <summary>
        ///     Valid terminator characters for CSI functional key sequences.
        /// </summary>
        private const string ValidCSITerminators = "ABCDFHPQRSu~ZE";

        /// <summary>
        ///   Matches all CSI keyboard formats:
        ///   ESC [ number ; modifiers : eventtype u/~
        ///   ESC [ number ; modifiers u/~
        ///   ESC [ number u/~
        ///   ESC [ 1 ; modifiers : eventtype letter
        ///   ESC [ 1 ; modifiers letter
        ///   ESC [ letter  (bare functional key, e.g. ESC[A for Up arrow)
        /// Valid terminator letters: A-D (arrows), F/H (End/Home), P-S (F1-F4)
        /// </summary>
        [GeneratedRegex(@$"^\x1B\[(?<keyCode>\d+)?([;:](?<modifiers>\d+)([;:](?<eventType>\d+))?)?(?<terminator>[{ValidCSITerminators}])$")]
        private static partial Regex GetCompletionPatternRegex();
    }
}
