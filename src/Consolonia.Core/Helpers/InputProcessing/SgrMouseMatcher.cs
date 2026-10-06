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
        : RegexAccumulatorMatcher<T, (int button, int x, int y, bool isRelease)>(onComplete, toRune, SgrPatternRegex())
    {
        private const string ButtonGroupName = "button";
        private const string XGroupName = "x";
        private const string YGroupName = "y";

        protected override (int button, int x, int y, bool isRelease)? OnTerminatorMatched(Match match)
        {
            int button = int.Parse(match.Groups[ButtonGroupName].Value);
            int x = int.Parse(match.Groups[XGroupName].Value);
            int y = int.Parse(match.Groups[YGroupName].Value);
            bool isRelease = match.Groups[TerminatorGroupName].Value == "m";

            return (button, x, y, isRelease);
        }

        public override bool TryFlush()
        {
            return
                Accumulator.Length !=
                0; //todo: this file is Claude production inspired by CsiKeyboardMatcher. I did not check this one well, but it works. However I have no idea why to pretend flushing if something accumulated
        }

        /// <summary>
        ///     Matches the SGR mouse format, both complete sequences and valid partial prefixes
        ///     accumulated so far (in which case the <c>terminator</c> group is not present):
        ///     ESC [ &lt; button ; x ; y M/m
        ///     ESC [ &lt;                       (valid prefix)
        ///     ESC [                            (valid prefix)
        ///     ESC                              (valid prefix)
        ///     Groups are nested so that a later group can only be present if the earlier ones
        ///     in the sequence are already present, preserving the correct ordering.
        /// </summary>
        [GeneratedRegex(
            @$"^\x1B(\[(<(?<{ButtonGroupName}>\d+)?(;(?<{XGroupName}>\d+)?(;(?<{YGroupName}>\d+)?)?)?(?<{TerminatorGroupName}>[Mm])?)?)?$")]
        private static partial Regex SgrPatternRegex();
    }
}