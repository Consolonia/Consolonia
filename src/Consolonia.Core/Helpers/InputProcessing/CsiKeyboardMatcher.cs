using System;
using System.Text;
using System.Text.RegularExpressions;

namespace Consolonia.Core.Helpers.InputProcessing
{
    /// <summary>
    ///     Matches Kitty keyboard protocol CSI u sequences and legacy CSI functional key sequences.
    ///     Formats:
    ///     CSI u:      ESC [ keycode ; modifiers u
    ///     CSI tilde:  ESC [ number ; modifiers ~     (Insert, Delete, PgUp, PgDn, F5-F12)
    ///     CSI letter: ESC [ 1 ; modifiers letter     (Arrows, Home, End, F1-F4)
    ///     CSI letter: ESC [ letter                    (unmodified arrows, Home, End, F1-F4)
    /// </summary>
    public partial class CsiKeyboardMatcher<T>(
        Action<(int keyCode, int modifiers, int eventType, char terminator)> onComplete,
        Func<T, Rune> toRune)
        : RegexAccumulatorMatcher<T, (int keyCode, int modifiers, int eventType, char terminator)>(onComplete, toRune,
            CsiPatternRegex())
    {
        private const string KeyCodeGroupName = "keyCode";
        private const string ModifiersGroupName = "modifiers";
        private const string EventTypeGroupName = "eventType";
        private const string Separator1GroupName = "sep1";
        private const string Separator2GroupName = "sep2";

        /// <summary>
        ///     Valid terminator characters for CSI functional key sequences.
        /// </summary>
        private const string ValidCsiTerminators = "ABCDFHPQRSu~ZE";

        protected override (int keyCode, int modifiers, int eventType, char terminator)? OnTerminatorMatched(
            Match match)
        {
            Group terminatorGroup = match.Groups[TerminatorGroupName];
            int keyCode = match.Groups[KeyCodeGroupName].Success
                ? int.Parse(match.Groups[KeyCodeGroupName].Value)
                : 0;
            char terminator = terminatorGroup.Value[0];

            // Don't match bracketed paste mode sequences, let PasteBlockMatcher handle them
            if (terminator == '~' && keyCode is 200 or 201)
                return null;

            int modifiers = match.Groups[ModifiersGroupName].Success
                ? int.Parse(match.Groups[ModifiersGroupName].Value)
                : 1;
            int eventType = match.Groups[EventTypeGroupName].Success
                ? int.Parse(match.Groups[EventTypeGroupName].Value)
                : 1;

            return (keyCode, modifiers, eventType, terminator);
        }

        public override bool TryFlush()
        {
            // Always return false: CSI sequences are auto-flushed on completion,
            // and returning true here would block lower-priority matchers (like
            // SgrMouseMatcher) from flushing when this matcher has partial data.
            return false;
        }

        /// <summary>
        ///     Matches all CSI keyboard formats, both complete sequences and valid partial prefixes
        ///     accumulated so far (in which case the <c>terminator</c> group is not present):
        ///     ESC [ number ; modifiers : eventtype u/~
        ///     ESC [ number ; modifiers u/~
        ///     ESC [ number u/~
        ///     ESC [ 1 ; modifiers : eventtype letter
        ///     ESC [ 1 ; modifiers letter
        ///     ESC [ letter  (bare functional key, e.g. ESC[A for Up arrow)
        ///     ESC [                            (valid prefix)
        ///     ESC                              (valid prefix)
        ///     Groups are nested so that a later group can only be present if the earlier ones
        ///     in the sequence are already present, preserving the correct ordering.
        ///     Valid terminator letters: A-D (arrows), F/H (End/Home), P-S (F1-F4)
        /// </summary>
        [GeneratedRegex(
            @$"^\x1B(\[(?<{KeyCodeGroupName}>\d+)?((?<{Separator1GroupName}>[;:])(?<{ModifiersGroupName}>\d+)?((?<{Separator2GroupName}>[;:])(?<{EventTypeGroupName}>\d+)?)?)?(?<{TerminatorGroupName}>[{ValidCsiTerminators}])?)?$")]
        private static partial Regex CsiPatternRegex();
    }
}