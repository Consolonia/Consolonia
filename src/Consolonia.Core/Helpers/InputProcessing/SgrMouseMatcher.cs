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
    public partial class SgrMouseMatcher<T>(
        Action<(int button, int x, int y, bool isRelease)> onComplete,
        Func<T, Rune> toRune)
        : MatcherWithComplete<T, (int button, int x, int y, bool isRelease)>(onComplete)
    {
        private readonly StringBuilder _accumulator = new();

        public override AppendResult Append(T input)
        {
            Rune rune = toRune(input);
            _accumulator.Append(rune);

            string current = _accumulator.ToString();

            // Match against the combined pattern: it matches both complete SGR mouse sequences
            // (terminator group present) and valid partial prefixes (terminator group absent).
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

            if (!match.Groups["terminator"].Success)
                // Valid prefix, but not complete yet
                return AppendResult.Match;

            int button = int.Parse(match.Groups["button"].Value);
            int x = int.Parse(match.Groups["x"].Value);
            int y = int.Parse(match.Groups["y"].Value);
            bool isRelease = match.Groups["terminator"].Value == "m";

            Complete((button, x, y, isRelease));
            _accumulator.Clear();
            return AppendResult.AutoFlushed;
        }

        public override bool TryFlush()
        {
            return _accumulator.Length != 0; //todo: this file is Claude production inspired by CsiKeyboardMatcher. I did not check this one well, but it works.
        }

        public override void Reset()
        {
            // Intentionally not clearing - same pattern as StartsEndsWithMatcher
        }

        public override string GetDebugInfo()
        {
            return $"{GetType().Name} {{{(_accumulator.Length == 0 ? "_" : _accumulator.ToString())}}}";
        }

        /// <summary>
        ///   Matches the SGR mouse format, both complete sequences and valid partial prefixes
        ///   accumulated so far (in which case the <c>terminator</c> group is not present):
        ///   ESC [ &lt; button ; x ; y M/m
        ///   ESC [ &lt;                       (valid prefix)
        ///   ESC [                            (valid prefix)
        ///   ESC                              (valid prefix)
        /// Groups are nested so that a later group can only be present if the earlier ones
        /// in the sequence are already present, preserving the correct ordering.
        /// </summary>
        [GeneratedRegex(
            """
             ^\x1B                     # ESC
             (
               \[                      # [
               <                       # <
               (?<button>\d+)?          # button digits
               (
                 ;                     # separator before x
                 (?<x>\d+)?             # x digits
                 (
                   ;                   # separator before y
                   (?<y>\d+)?           # y digits
                 )?
               )?
               (?<terminator>[Mm])?     # terminator letter
             )?
             $
             """,
            RegexOptions.IgnorePatternWhitespace)]
        private static partial Regex GetPatternRegex();
    }
}
