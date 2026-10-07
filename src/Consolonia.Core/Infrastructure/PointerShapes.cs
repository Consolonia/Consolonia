using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Avalonia.Input;

namespace Consolonia.Core.Infrastructure
{
    /// <summary>
    ///     Mouse pointer shapes a terminal can draw itself, from Kitty's pointer shape protocol
    ///     (OSC 22), and how Avalonia's cursors map onto them.
    /// </summary>
    /// <remarks>
    ///     The names are CSS <c>cursor</c> values, which is what Kitty, foot, WezTerm and Ghostty
    ///     accept. A terminal that has a shape draws the pointer as a real cursor; for one it does
    ///     not have, Consolonia keeps drawing its character pointer instead.
    /// </remarks>
    public static class PointerShapes
    {
        /// <summary>The shape a terminal shows when nothing has asked for another.</summary>
        public const string Default = "default";

        private static readonly Dictionary<StandardCursorType, string> ShapeOf = new()
        {
            [StandardCursorType.Arrow] = Default,
            [StandardCursorType.Ibeam] = "text",
            [StandardCursorType.Hand] = "pointer",
            [StandardCursorType.Cross] = "crosshair",
            [StandardCursorType.Help] = "help",
            [StandardCursorType.No] = "not-allowed",
            [StandardCursorType.Wait] = "wait",
            [StandardCursorType.AppStarting] = "progress",
            [StandardCursorType.SizeAll] = "move",
            [StandardCursorType.SizeNorthSouth] = "ns-resize",
            [StandardCursorType.SizeWestEast] = "ew-resize",
            [StandardCursorType.TopSide] = "n-resize",
            [StandardCursorType.BottomSide] = "s-resize",
            [StandardCursorType.LeftSide] = "w-resize",
            [StandardCursorType.RightSide] = "e-resize",
            [StandardCursorType.TopLeftCorner] = "nw-resize",
            [StandardCursorType.TopRightCorner] = "ne-resize",
            [StandardCursorType.BottomLeftCorner] = "sw-resize",
            [StandardCursorType.BottomRightCorner] = "se-resize",
            [StandardCursorType.DragCopy] = "copy",
            [StandardCursorType.DragLink] = "alias",
            [StandardCursorType.DragMove] = "grabbing"
            // UpArrow and None have no CSS equivalent: they always use the character pointer.
        };

        /// <summary>
        ///     xterm's names for the same pointers: X11 cursor-font names, which xterm takes in place
        ///     of CSS ones. A shape with no X11 counterpart is left out, and falls back to the
        ///     character pointer there.
        /// </summary>
        private static readonly Dictionary<string, string> X11Names = new(StringComparer.Ordinal)
        {
            [Default] = "left_ptr",
            ["text"] = "xterm",
            ["pointer"] = "hand2",
            ["crosshair"] = "crosshair",
            ["help"] = "question_arrow",
            ["not-allowed"] = "X_cursor",
            ["wait"] = "watch",
            ["progress"] = "watch",
            ["move"] = "fleur",
            ["grabbing"] = "fleur",
            ["ns-resize"] = "sb_v_double_arrow",
            ["ew-resize"] = "sb_h_double_arrow",
            ["n-resize"] = "top_side",
            ["s-resize"] = "bottom_side",
            ["w-resize"] = "left_side",
            ["e-resize"] = "right_side",
            ["nw-resize"] = "top_left_corner",
            ["ne-resize"] = "top_right_corner",
            ["sw-resize"] = "bottom_left_corner",
            ["se-resize"] = "bottom_right_corner"
        };

        /// <summary>
        ///     The terminals known to draw OSC 22 pointers, from which version, and whether they take
        ///     X11 names rather than CSS ones.
        /// </summary>
        /// <remarks>
        ///     For terminals that support the protocol but do not answer Kitty's support query, which
        ///     is everything but Kitty: without this they would be taken for terminals without it.
        ///     iTerm2 (3.5, its own subset of names) and WezTerm (unconfirmed) are deliberately absent
        ///     until they have been tried; VTE, Alacritty and Windows Terminal do not support OSC 22.
        /// </remarks>
        private static readonly (string Name, Version Since, bool X11)[] KnownTerminals =
        [
            ("kitty", new Version(0, 31), false),
            ("foot", new Version(1, 12), false),
            ("ghostty", new Version(1, 0), false),
            ("xterm", new Version(367, 0), true)
        ];

        private static readonly Regex XtVersionRegex = new(@"\u001bP>\|(?<text>[^\u001b]*)\u001b\\");

        private static readonly Regex NameAndVersionRegex =
            new(@"^(?<name>[A-Za-z][A-Za-z0-9_-]*)[\s(]*(?<version>[0-9][0-9.]*)?");

        // At least one flag: an empty OSC 22 reply says nothing about any shape, so it must not be
        // taken for an answer and cut off the XTVERSION and environment fallbacks.
        private static readonly Regex QueryAnswerRegex = new(@"\u001b\]22;[01](,[01])*(\u001b\\|\u0007)");

        /// <summary>
        ///     The longest a support query's OSC string (<c>22;?</c> and the names) may be. libtsm, and
        ///     so kmscon, keeps the first 127 bytes of an OSC string and silently drops the rest, so the
        ///     names past them would go unanswered and be taken for shapes the terminal does not have.
        /// </summary>
        private const int MaxQueryLength = 127;

        private const string QueryPrefix = "22;?";

        /// <summary>Every shape any Avalonia cursor maps to: what a probe asks the terminal about.</summary>
        public static IReadOnlyList<string> Used { get; } = ShapeOf.Values.Distinct().ToArray();

        /// <summary>
        ///     <see cref="Used" />, split into support queries short enough for every terminal to read
        ///     whole, in order. A probe sends one query per batch, and each is answered on its own.
        /// </summary>
        public static IReadOnlyList<IReadOnlyList<string>> QueryBatches { get; } = Batch(Used);

        private static IReadOnlyList<IReadOnlyList<string>> Batch(IReadOnlyList<string> shapes)
        {
            var batches = new List<IReadOnlyList<string>>();
            var batch = new List<string>();
            int length = QueryPrefix.Length;

            foreach (string shape in shapes)
            {
                int added = (batch.Count > 0 ? 1 : 0) + shape.Length;
                if (batch.Count > 0 && length + added > MaxQueryLength)
                {
                    batches.Add(batch);
                    batch = new List<string>();
                    length = QueryPrefix.Length;
                    added = shape.Length;
                }

                batch.Add(shape);
                length += added;
            }

            if (batch.Count > 0)
                batches.Add(batch);
            return batches;
        }

        /// <summary>
        ///     The terminal pointer shape for <paramref name="cursor" />, or null when there is none and
        ///     the character pointer has to stand in for it.
        /// </summary>
        public static string For(StandardCursorType cursor)
        {
            return ShapeOf.GetValueOrDefault(cursor);
        }

        /// <summary>The X11 cursor-font name xterm uses for <paramref name="shape" />, or null when it has none.</summary>
        public static string ToX11(string shape)
        {
            return X11Names.GetValueOrDefault(shape);
        }

        /// <summary>
        ///     The shapes <paramref name="terminal" /> is known to draw when it did not answer the
        ///     support query, as CSS names, and whether it wants them written as X11 names. Nothing for
        ///     a terminal not known to support OSC 22, or older than the version that added it.
        /// </summary>
        /// <remarks>
        ///     A terminal identified without a version -- from its TERM alone -- is taken to be recent
        ///     enough: every one listed has supported OSC 22 for years.
        /// </remarks>
        public static (IReadOnlySet<string> Supported, bool UsesX11Names) KnownSupport(TerminalIdentity terminal)
        {
            (IReadOnlySet<string>, bool) none = (new HashSet<string>(), false);
            if (terminal is null)
                return none;

            foreach ((string name, Version since, bool x11) in KnownTerminals)
            {
                if (!string.Equals(name, terminal.Name, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (terminal.Version != null && terminal.Version < since)
                    return none;

                IEnumerable<string> shapes = x11 ? Used.Where(s => X11Names.ContainsKey(s)) : Used;
                return (new HashSet<string>(shapes, StringComparer.Ordinal), x11);
            }

            return none;
        }

        /// <summary>
        ///     What a terminal's answers to the probe mean: the shapes it can draw, as CSS names, and
        ///     whether it wants them written as X11 names.
        /// </summary>
        /// <param name="answers">
        ///     Everything the terminal sent back to <see cref="Text.Esc.QueryPointerShapes" />,
        ///     <see cref="Text.Esc.QueryTerminalVersion" /> and the Device Attributes sentinel.
        /// </param>
        /// <param name="environment">Reads an environment variable; the fallback when nothing identifies the terminal.</param>
        /// <remarks>
        ///     An answer to the support query is authoritative, shape by shape. Most terminals that
        ///     draw OSC 22 pointers do not answer it, though, so without one the terminal is
        ///     identified -- from its XTVERSION answer, or failing that its environment -- and looked
        ///     up in <see cref="KnownSupport" />. Shared by every console that probes, so they agree.
        /// </remarks>
        public static (IReadOnlySet<string> Supported, bool UsesX11Names) Detect(string answers,
            Func<string, string> environment)
        {
            if (QueryAnswerRegex.IsMatch(answers))
                return (ParseQueryReplies(answers, QueryBatches), false);

            return KnownSupport(ParseXtVersion(answers) ?? FromEnvironment(environment));
        }

        /// <summary>
        ///     Reads a terminal's answer to XTVERSION (<c>CSI &gt; q</c>), <c>DCS &gt; | text ST</c>, where
        ///     the text is the terminal's name and version: <c>XTerm(367)</c>, <c>kitty(0.31.0)</c>,
        ///     <c>foot(1.12.1)</c>, <c>ghostty 1.1.0</c>.
        /// </summary>
        /// <returns>Who answered, or null for an answer that does not parse.</returns>
        public static TerminalIdentity ParseXtVersion(string reply)
        {
            Match match = XtVersionRegex.Match(reply);
            return match.Success ? ParseNameAndVersion(match.Groups["text"].Value) : null;
        }

        /// <summary>
        ///     The terminal as its environment describes it: for when XTVERSION goes unanswered.
        /// </summary>
        /// <remarks>
        ///     Only variables that name a terminal reliably. TERM=xterm-256color says nothing -- nearly
        ///     every terminal sets it -- so xterm is recognised by XTERM_VERSION, which only xterm sets.
        /// </remarks>
        public static TerminalIdentity FromEnvironment(Func<string, string> variable)
        {
            string xtermVersion = variable("XTERM_VERSION");
            if (!string.IsNullOrEmpty(xtermVersion))
                return ParseNameAndVersion(xtermVersion);

            string program = variable("TERM_PROGRAM");
            if (string.Equals(program, "ghostty", StringComparison.OrdinalIgnoreCase))
                return new TerminalIdentity("ghostty", ParseVersion(variable("TERM_PROGRAM_VERSION")));

            string term = variable("TERM") ?? string.Empty;
            if (term.Equals("xterm-ghostty", StringComparison.OrdinalIgnoreCase))
                return new TerminalIdentity("ghostty", null);
            if (term.Equals("foot", StringComparison.OrdinalIgnoreCase) ||
                term.StartsWith("foot-", StringComparison.OrdinalIgnoreCase))
                return new TerminalIdentity("foot", null);
            if (term.Equals("xterm-kitty", StringComparison.OrdinalIgnoreCase) ||
                !string.IsNullOrEmpty(variable("KITTY_WINDOW_ID")))
                return new TerminalIdentity("kitty", null);

            return null;
        }

        private static TerminalIdentity ParseNameAndVersion(string text)
        {
            Match match = NameAndVersionRegex.Match(text.Trim());
            return match.Success
                ? new TerminalIdentity(match.Groups["name"].Value,
                    ParseVersion(match.Groups["version"].Value))
                : null;
        }

        /// <summary><c>1.12.1</c>, or xterm's bare patch number <c>367</c>, as a version.</summary>
        private static Version ParseVersion(string text)
        {
            if (string.IsNullOrEmpty(text))
                return null;
            text = text.TrimEnd('.');
            if (int.TryParse(text, out int major))
                return new Version(major, 0);
            return Version.TryParse(text, out Version version) ? version : null;
        }

        /// <summary>
        ///     Reads a terminal's answer to a support query (<c>OSC 22 ; ? a,b,c ST</c>), which is
        ///     <c>OSC 22 ; 1,0,1 ST</c>: one flag per name asked, in order.
        /// </summary>
        /// <returns>The names the terminal said it has; empty for an answer that does not parse.</returns>
        public static IReadOnlySet<string> ParseQueryReply(string reply, IReadOnlyList<string> asked)
        {
            const string prefix = "\u001b]22;";
            int start = reply.IndexOf(prefix, StringComparison.Ordinal);
            if (start < 0)
                return new HashSet<string>();

            start += prefix.Length;
            int end = reply.IndexOfAny(['\u001b', '\u0007'], start);
            if (end < 0)
                return new HashSet<string>();

            string[] flags = reply[start..end].Split(',');
            var supported = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < flags.Length && i < asked.Count; i++)
                if (flags[i].Trim() == "1")
                    supported.Add(asked[i]);

            return supported;
        }

        /// <summary>
        ///     Reads the answers to several support queries, one per batch of names: the first reply
        ///     answers the first batch, and so on. Empty replies flag nothing and are passed over.
        /// </summary>
        /// <returns>The names the terminal said it has; a batch with no reply contributes none.</returns>
        public static IReadOnlySet<string> ParseQueryReplies(string answers,
            IReadOnlyList<IReadOnlyList<string>> batches)
        {
            var supported = new HashSet<string>(StringComparer.Ordinal);
            MatchCollection replies = QueryAnswerRegex.Matches(answers);
            for (int i = 0; i < replies.Count && i < batches.Count; i++)
                supported.UnionWith(ParseQueryReply(replies[i].Value, batches[i]));

            return supported;
        }
    }

    /// <summary>A terminal's name as it gave it (compared without regard to case), and its version where it gave one.</summary>
    public sealed record TerminalIdentity(string Name, Version Version);
}