using System;
using System.Text;
using System.Text.RegularExpressions;

namespace Consolonia.Core.Helpers.InputProcessing
{
    /// <summary>
    ///     Matches Kitty keyboard protocol CSI u sequences and legacy CSI functional key sequences.
    ///     Formats:
    ///     CSI u:      ESC [ keycode[:shifted[:base]] ; modifiers u
    ///     CSI tilde:  ESC [ number ; modifiers ~     (Insert, Delete, PgUp, PgDn, F5-F12)
    ///     CSI letter: ESC [ 1 ; modifiers letter     (Arrows, Home, End, F1-F4)
    ///     CSI letter: ESC [ letter                    (unmodified arrows, Home, End, F1-F4)
    /// </summary>
    public partial class CsiKeyboardMatcher<T>(
        Action<(int keyCode, int modifiers, int eventType, char terminator, int shiftedKeyCode)> onComplete,
        Func<T, Rune> toRune)
        : RegexAccumulatorMatcher<T, (int keyCode, int modifiers, int eventType, char terminator, int shiftedKeyCode)>(
            onComplete, toRune,
            CsiPatternRegex())
    {
        private const string KeyCodeGroupName = "keyCode";
        private const string ShiftedKeyCodeGroupName = "shiftedKeyCode";
        private const string BaseKeyCodeGroupName = "baseKeyCode";
        private const string ModifiersGroupName = "modifiers";
        private const string EventTypeGroupName = "eventType";
        private const string Separator1GroupName = "sep1";
        private const string Separator2GroupName = "sep2";

        /// <summary>
        ///     Valid terminator characters for CSI functional key sequences.
        /// </summary>
        private const string ValidCsiTerminators = "ABCDFHPQRSu~ZE";

        protected override (int keyCode,
            int modifiers,
            int eventType,
            char terminator,
            int shiftedKeyCode)? OnTerminatorMatched(Match match)
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

            Group shiftedGroup = match.Groups[ShiftedKeyCodeGroupName];
            int shiftedKeyCode = shiftedGroup.Success && shiftedGroup.Length > 0
                ? int.Parse(shiftedGroup.Value)
                : 0;

            return (keyCode, modifiers, eventType, terminator, shiftedKeyCode);
        }

        public override bool TryFlush()
        {
            // A lone ESC is still ambiguous: it may turn out to be a solo escape or the leading half of an
            // Alt+key combination, and the matchers below have to be able to keep collecting it across input
            // chunks. From the CSI introducer on the sequence is ours, so we pretend to have flushed it -
            // the same lie StartsEndsWithMatcher tells - to keep the matchers below from emitting the bytes
            // of the still incomplete sequence on their own. Other accumulator matchers (SgrMouseMatcher)
            // do not lose their partial data to that, because their Reset keeps it.
            return Accumulator.Length > 1 && Accumulator[1] == '[';
        }

        /// <summary>
        ///     Matches all CSI keyboard formats, both complete sequences and valid partial prefixes
        ///     accumulated so far (in which case the <c>terminator</c> group is not present):
        ///     ESC [ number : shifted : base ; modifiers : eventtype u   (Kitty alternate keys)
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
            @$"^\x1B(\[(?<{KeyCodeGroupName}>\d+)?(:(?<{ShiftedKeyCodeGroupName}>\d*)(:(?<{BaseKeyCodeGroupName}>\d*))?)?((?<{Separator1GroupName}>;)(?<{ModifiersGroupName}>\d+)?((?<{Separator2GroupName}>[;:])(?<{EventTypeGroupName}>\d+)?)?)?(?<{TerminatorGroupName}>[{ValidCsiTerminators}])?)?$")]
        private static partial Regex CsiPatternRegex();
    }
}