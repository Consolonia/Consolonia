using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Input;
using Consolonia.Core.Dummy;
using Consolonia.Core.Infrastructure;
using Consolonia.Core.Text;
using NUnit.Framework;

namespace Consolonia.Core.Tests
{
    [TestFixture]
    public class PointerShapesTests
    {
        // Kitty's pointer shape names: the CSS cursor values.
        private static readonly string[] KittyShapes =
        [
            "alias", "cell", "copy", "crosshair", "default", "e-resize", "ew-resize", "grab", "grabbing",
            "help", "move", "n-resize", "ne-resize", "nesw-resize", "no-drop", "not-allowed", "ns-resize",
            "nw-resize", "nwse-resize", "pointer", "progress", "s-resize", "se-resize", "sw-resize", "text",
            "vertical-text", "w-resize", "wait", "zoom-in", "zoom-out"
        ];

        [Test]
        public void EveryMappedCursorIsAKittyShape()
        {
            foreach (StandardCursorType cursor in Enum.GetValues<StandardCursorType>())
            {
                string shape = PointerShapes.For(cursor);
                if (shape != null)
                    Assert.That(KittyShapes, Does.Contain(shape), cursor.ToString());
            }
        }

        [Test]
        public void CommonCursorsMapToTheirCssNames()
        {
            Assert.That(PointerShapes.For(StandardCursorType.Arrow), Is.EqualTo("default"));
            Assert.That(PointerShapes.For(StandardCursorType.Ibeam), Is.EqualTo("text"));
            Assert.That(PointerShapes.For(StandardCursorType.Hand), Is.EqualTo("pointer"));
            Assert.That(PointerShapes.For(StandardCursorType.SizeWestEast), Is.EqualTo("ew-resize"));
            Assert.That(PointerShapes.For(StandardCursorType.BottomRightCorner), Is.EqualTo("se-resize"));
        }

        [Test]
        public void CursorsWithNoCssEquivalentHaveNoShape()
        {
            // These stay character pointers whatever the terminal supports.
            Assert.That(PointerShapes.For(StandardCursorType.UpArrow), Is.Null);
            Assert.That(PointerShapes.For(StandardCursorType.None), Is.Null);
        }

        [Test]
        public void TheProbeAsksAboutEachShapeOnce()
        {
            Assert.That(PointerShapes.Used, Is.Unique);
            Assert.That(PointerShapes.Used, Does.Contain("default"));
        }

        [Test]
        public void AReplyGivesTheShapesFlaggedOneInTheOrderAsked()
        {
            string[] asked = ["default", "text", "pointer"];

            IReadOnlySet<string> supported = PointerShapes.ParseQueryReply("\u001b]22;1,0,1\u001b\\", asked);

            Assert.That(supported, Is.EquivalentTo(new[] { "default", "pointer" }));
        }

        [Test]
        public void AReplyEndedWithBelIsReadToo()
        {
            IReadOnlySet<string> supported = PointerShapes.ParseQueryReply("\u001b]22;1,1\u0007", ["default", "text"]);

            Assert.That(supported, Is.EquivalentTo(new[] { "default", "text" }));
        }

        [Test]
        public void AReplyAmongOtherInputIsFound()
        {
            // The Device Attributes sentinel may arrive in the same read.
            IReadOnlySet<string> supported =
                PointerShapes.ParseQueryReply("\u001b]22;0,1\u001b\\\u001b[?62;22c", ["default", "text"]);

            Assert.That(supported, Is.EquivalentTo(new[] { "text" }));
        }

        [TestCase("")]
        [TestCase("\u001b[?62;22c")]
        [TestCase("\u001b]22;1,1")]
        public void NoCompleteReplyMeansNoShapes(string reply)
        {
            Assert.That(PointerShapes.ParseQueryReply(reply, ["default", "text"]), Is.Empty);
        }

        [Test]
        public void AShortReplyCoversOnlyTheNamesItAnswers()
        {
            IReadOnlySet<string> supported = PointerShapes.ParseQueryReply("\u001b]22;1\u001b\\", ["default", "text"]);

            Assert.That(supported, Is.EquivalentTo(new[] { "default" }));
        }

        [Test]
        public void TheSequencesAreKittysOsc22()
        {
            Assert.That(Esc.SetPointerShape("text"), Is.EqualTo("\u001b]22;text\u001b\\"));
            Assert.That(Esc.QueryPointerShapes(["default", "text"]), Is.EqualTo("\u001b]22;?default,text\u001b\\"));
            Assert.That(Esc.ResetPointerShape, Is.EqualTo("\u001b]22;\u001b\\"));
        }

        [TestCase("\u001bP>|XTerm(367)\u001b\\", "xterm", "367.0")]
        [TestCase("\u001bP>|kitty(0.31.0)\u001b\\", "kitty", "0.31.0")]
        [TestCase("\u001bP>|foot(1.12.1)\u001b\\", "foot", "1.12.1")]
        [TestCase("\u001bP>|ghostty 1.1.0\u001b\\", "ghostty", "1.1.0")]
        [TestCase("\u001bP>|WezTerm 20240203-110809-5046fc22\u001b\\", "wezterm", "20240203.0")]
        public void XtVersionNamesTheTerminal(string reply, string name, string version)
        {
            TerminalIdentity terminal = PointerShapes.ParseXtVersion("\u001b[?62;22c" + reply);

            Assert.That(terminal.Name, Is.EqualTo(name).IgnoreCase);
            Assert.That(terminal.Version, Is.EqualTo(Version.Parse(version)));
        }

        [Test]
        public void NoXtVersionAnswerNamesNothing()
        {
            Assert.That(PointerShapes.ParseXtVersion("\u001b[?62;22c"), Is.Null);
        }

        [Test]
        public void CssTerminalsFromTheirFirstOsc22VersionGetEveryShape()
        {
            foreach ((string name, string version) in new[] { ("foot", "1.12"), ("ghostty", "1.0"), ("kitty", "0.31") })
            {
                (IReadOnlySet<string> supported, bool x11) =
                    PointerShapes.KnownSupport(new TerminalIdentity(name, Version.Parse(version)));

                Assert.That(supported, Is.EquivalentTo(PointerShapes.Used), name);
                Assert.That(x11, Is.False, name);
            }
        }

        [Test]
        public void OlderVersionsGetNothing()
        {
            Assert.That(PointerShapes.KnownSupport(new TerminalIdentity("foot", new Version(1, 11))).Supported,
                Is.Empty);
            Assert.That(PointerShapes.KnownSupport(new TerminalIdentity("xterm", new Version(366, 0))).Supported,
                Is.Empty);
        }

        [Test]
        public void XtermGetsTheShapesThatHaveX11Names()
        {
            (IReadOnlySet<string> supported, bool x11) =
                PointerShapes.KnownSupport(new TerminalIdentity("xterm", new Version(390, 0)));

            Assert.That(x11, Is.True);
            Assert.That(supported, Does.Contain("text").And.Contain("default").And.Contain("ew-resize"));
            Assert.That(supported, Does.Not.Contain("copy"), "copy has no X11 cursor-font name");
            Assert.That(supported.All(s => PointerShapes.ToX11(s) != null));
        }

        [TestCase("default", "left_ptr")]
        [TestCase("text", "xterm")]
        [TestCase("pointer", "hand2")]
        [TestCase("se-resize", "bottom_right_corner")]
        public void X11NamesAreXtermsCursorFont(string css, string x11)
        {
            Assert.That(PointerShapes.ToX11(css), Is.EqualTo(x11));
        }

        [TestCase("iterm2")]
        [TestCase("wezterm")]
        [TestCase("vte")]
        public void UntestedOrUnsupportedTerminalsGetNothing(string name)
        {
            Assert.That(PointerShapes.KnownSupport(new TerminalIdentity(name, new Version(99, 0))).Supported, Is.Empty);
        }

        [Test]
        public void AnUnknownTerminalGetsNothing()
        {
            Assert.That(PointerShapes.KnownSupport(null).Supported, Is.Empty);
        }

        [TestCase("XTERM_VERSION", "XTerm(390)", "xterm")]
        [TestCase("TERM_PROGRAM", "ghostty", "ghostty")]
        [TestCase("TERM", "xterm-ghostty", "ghostty")]
        [TestCase("TERM", "foot", "foot")]
        [TestCase("TERM", "foot-direct", "foot")]
        [TestCase("TERM", "xterm-kitty", "kitty")]
        [TestCase("KITTY_WINDOW_ID", "1", "kitty")]
        public void TheEnvironmentCanNameTheTerminal(string variable, string value, string name)
        {
            TerminalIdentity terminal = PointerShapes.FromEnvironment(v => v == variable ? value : null);

            Assert.That(terminal?.Name, Is.EqualTo(name).IgnoreCase);
        }

        [Test]
        public void ACommonTermSaysNothing()
        {
            // Nearly every terminal sets TERM=xterm-256color, xterm or not.
            Assert.That(PointerShapes.FromEnvironment(v => v == "TERM" ? "xterm-256color" : null), Is.Null);
        }

        private static string NoEnvironment(string _)
        {
            return null;
        }

        /// <summary>The terminal's answer to each support query in turn, flagging <paramref name="has" />.</summary>
        private static string QueryAnswers(Func<string, bool> has)
        {
            return string.Concat(PointerShapes.QueryBatches.Select(batch =>
                "\u001b]22;" + string.Join(",", batch.Select(s => has(s) ? "1" : "0")) + "\u001b\\"));
        }

        [Test]
        public void EachSupportQueryFitsLibtsmsOscBuffer()
        {
            // libtsm (kmscon) keeps 127 bytes of an OSC string; the names past them went unanswered.
            foreach (IReadOnlyList<string> batch in PointerShapes.QueryBatches)
            {
                string query = Esc.QueryPointerShapes(batch);
                string oscString = query["\u001b]".Length..^"\u001b\\".Length];
                Assert.That(oscString.Length, Is.LessThanOrEqualTo(127), oscString);
            }
        }

        [Test]
        public void TheSupportQueriesAskAboutEveryShapeInOrder()
        {
            Assert.That(PointerShapes.QueryBatches.SelectMany(b => b), Is.EqualTo(PointerShapes.Used));
            Assert.That(PointerShapes.QueryBatches.All(b => b.Count > 0));
        }

        [Test]
        public void EachReplyAnswersItsOwnBatch()
        {
            string[][] batches = [["default", "text"], ["nw-resize", "se-resize"]];

            IReadOnlySet<string> supported = PointerShapes.ParseQueryReplies(
                "\u001b]22;0,1\u001b\\\u001b]22;1,0\u001b\\\u001b[?62;22c", batches);

            Assert.That(supported, Is.EquivalentTo(new[] { "text", "nw-resize" }));
        }

        [Test]
        public void ABatchWithNoReplyHasNoShapes()
        {
            string[][] batches = [["default", "text"], ["nw-resize", "se-resize"]];

            IReadOnlySet<string> supported =
                PointerShapes.ParseQueryReplies("\u001b]22;1,1\u001b\\\u001b[?62;22c", batches);

            Assert.That(supported, Is.EquivalentTo(new[] { "default", "text" }));
        }

        [Test]
        public void DetectTakesAQueryAnswerAsAuthoritative()
        {
            string answers = QueryAnswers(s => s == "text") + "\u001bP>|XTerm(390)\u001b\\\u001b[?62;22c";

            (IReadOnlySet<string> supported, bool x11) = PointerShapes.Detect(answers, NoEnvironment);

            Assert.That(supported, Is.EquivalentTo(new[] { "text" }));
            Assert.That(x11, Is.False, "an answered query is in CSS names, whoever answered");
        }

        [Test]
        public void DetectReadsShapesFromEveryQuery()
        {
            // The corners come late in the list: they are what a single, truncated query lost.
            string[] corners = ["nw-resize", "ne-resize", "sw-resize", "se-resize"];

            (IReadOnlySet<string> supported, _) =
                PointerShapes.Detect(QueryAnswers(_ => true) + "\u001b[?62;22c", NoEnvironment);

            Assert.That(supported, Is.EquivalentTo(PointerShapes.Used));
            Assert.That(supported, Is.SupersetOf(corners));
        }

        [Test]
        public void DetectFallsBackToXtVersion()
        {
            (IReadOnlySet<string> supported, bool x11) =
                PointerShapes.Detect("\u001bP>|XTerm(390)\u001b\\\u001b[?62;22c", NoEnvironment);

            Assert.That(supported, Does.Contain("text"));
            Assert.That(x11, Is.True);
        }

        [TestCase("\u001b]22;\u001b\\")]
        [TestCase("\u001b]22;\u0007")]
        public void DetectFallsBackToXtVersionPastAnEmptyQueryAnswer(string emptyAnswer)
        {
            // An empty OSC 22 reply flags no shape at all, so it is not an answer to the query.
            (IReadOnlySet<string> supported, bool x11) =
                PointerShapes.Detect(emptyAnswer + "\u001bP>|XTerm(390)\u001b\\\u001b[?62;22c", NoEnvironment);

            Assert.That(supported, Does.Contain("text"));
            Assert.That(x11, Is.True);
        }

        [Test]
        public void DetectFallsBackToTheEnvironment()
        {
            (IReadOnlySet<string> supported, _) =
                PointerShapes.Detect("\u001b[?62;22c", v => v == "TERM" ? "foot" : null);

            Assert.That(supported, Is.EquivalentTo(PointerShapes.Used));
        }

        [Test]
        public void DetectFindsNothingWhereOnlyTheSentinelIsAnswered()
        {
            // Classic conhost, VTE, Alacritty, Windows Terminal, kmscon.
            Assert.That(PointerShapes.Detect("\u001b[?1;0c", NoEnvironment).Supported, Is.Empty);
            Assert.That(PointerShapes.Detect(string.Empty, NoEnvironment).Supported, Is.Empty);
        }

        [Test]
        public void AConsoleWithoutTheProtocolHasNoShapes()
        {
            IConsoleOutput console = new DummyConsoleOutput();

            Assert.That(console.SupportedPointerShapes, Is.Empty);
            Assert.DoesNotThrow(() => console.SetPointerShape("text"));
            Assert.That(PointerShapes.Used.All(s => !console.SupportedPointerShapes.Contains(s)));
        }
    }
}