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
            
            Match match = GetPatternRegex().Match(current);
            if (!match.Success)
            {
                // Not a match, remove the last character
                _accumulator.Length--;
                if (_accumulator.Length > 0)
                {
                    // We had accumulated some chars but this one broke the pattern
                    _accumulator.Clear();
                }

                return AppendResult.NoMatch;
            }

            Group terminatorGroup = match.Groups["terminator"];
            if (!terminatorGroup.Success)
                // Valid prefix, but not complete yet
                return AppendResult.Match;

            int keyCode = match.Groups["keyCode"].Success ? int.Parse(match.Groups["keyCode"].Value) : 0;
            char terminator = terminatorGroup.Value[0];

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
        ///   Matches all CSI keyboard formats, both complete sequences and valid partial prefixes
        ///   accumulated so far (in which case the <c>terminator</c> group is not present):
        ///   ESC [ number ; modifiers : eventtype u/~
        ///   ESC [ number ; modifiers u/~
        ///   ESC [ number u/~
        ///   ESC [ 1 ; modifiers : eventtype letter
        ///   ESC [ 1 ; modifiers letter
        ///   ESC [ letter  (bare functional key, e.g. ESC[A for Up arrow)
        ///   ESC [                            (valid prefix)
        ///   ESC                              (valid prefix)
        /// Groups are nested so that a later group can only be present if the earlier ones
        /// in the sequence are already present, preserving the correct ordering.
        /// Valid terminator letters: A-D (arrows), F/H (End/Home), P-S (F1-F4)
        /// </summary>
        [GeneratedRegex(
            @$"^\x1B(\[(?<keyCode>\d+)?((?<sep1>[;:])(?<modifiers>\d+)?((?<sep2>[;:])(?<eventType>\d+)?)?)?(?<terminator>[{ValidCSITerminators}])?)?$")]
        private static partial Regex GetPatternRegex();
    }
}
