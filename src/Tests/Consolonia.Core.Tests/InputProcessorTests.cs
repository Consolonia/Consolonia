#nullable enable

using System;
using System.Collections.Generic;
using System.Text;
using Consolonia.Core.Helpers.InputProcessing;
using NUnit.Framework;

namespace Consolonia.Core.Tests
{
    [TestFixture]
    public class InputProcessorTests
    {
        [Test]
        public void RealLifeTest()
        {
            List<(Type, ConsoleKey?, string?, string?)> output = [];

            List<(Type, ConsoleKey?, string?, string?)> expected =
            [
                (typeof(GenericMatcher<ConsoleKeyInfo>), ConsoleKey.RightArrow, null, null),
                (typeof(GenericMatcher<ConsoleKeyInfo>), ConsoleKey.UpArrow, null, null),
                (typeof(TextInputMatcher<ConsoleKeyInfo>), null, "a", null),
                (typeof(TextInputMatcher<ConsoleKeyInfo>), null, "b", null),
                (typeof(GenericMatcher<ConsoleKeyInfo>), ConsoleKey.RightArrow, null, null),
                (typeof(PasteBlockMatcher<ConsoleKeyInfo>), null, null, "qwe"),
                (typeof(TextInputMatcher<ConsoleKeyInfo>), null, "abc", null)
            ];

            var chunkedDataProcessor = new InputProcessor<ConsoleKeyInfo>([
                new PasteBlockMatcher<ConsoleKeyInfo>(
                    str => { output.Add((typeof(PasteBlockMatcher<ConsoleKeyInfo>), null, null, str)); },
                    arg => new Rune(arg.KeyChar)),
                new TextInputMatcher<ConsoleKeyInfo>(
                    tuple => { output.Add((typeof(TextInputMatcher<ConsoleKeyInfo>), null, tuple.Item1, null)); },
                    arg => new Rune(arg.KeyChar)),
                new GenericMatcher<ConsoleKeyInfo>(info =>
                {
                    output.Add((typeof(GenericMatcher<ConsoleKeyInfo>), info.Key, null, null));
                })
            ]);

            // sequence: right, up, a,

            IReadOnlyCollection<ConsoleKeyInfo> chunk =
            [
                new('\0', ConsoleKey.RightArrow, false, false, false),
                new('\0', ConsoleKey.UpArrow, false, false, false),
                new('a', ConsoleKey.A, false, false, false)
            ];

            chunkedDataProcessor.ProcessChunk(chunk);

            // b, right, 

            chunk =
            [
                new ConsoleKeyInfo('b', ConsoleKey.B, false, false, false),
                new ConsoleKeyInfo('\0', ConsoleKey.RightArrow, false, false, false),
                new ConsoleKeyInfo('', ConsoleKey.Escape, false, false, false)
            ];

            chunkedDataProcessor.ProcessChunk(chunk);

            // [200~qwe[201~abc

            chunk =
            [
                new ConsoleKeyInfo('[', ConsoleKey.Oem8, false, false, false),
                new ConsoleKeyInfo('2', ConsoleKey.D2, false, false, false),
                new ConsoleKeyInfo('0', ConsoleKey.D0, false, false, false),
                new ConsoleKeyInfo('0', ConsoleKey.D0, false, false, false),
                new ConsoleKeyInfo('~', ConsoleKey.Oem7, false, false, false),
                new ConsoleKeyInfo('q', ConsoleKey.Q, false, false, false),
                new ConsoleKeyInfo('w', ConsoleKey.W, false, false, false),
                new ConsoleKeyInfo('e', ConsoleKey.E, false, false, false),
                new ConsoleKeyInfo('', ConsoleKey.Escape, false, false, false),
                new ConsoleKeyInfo('[', ConsoleKey.Oem8, false, false, false),
                new ConsoleKeyInfo('2', ConsoleKey.D2, false, false, false),
                new ConsoleKeyInfo('0', ConsoleKey.D0, false, false, false),
                new ConsoleKeyInfo('1', ConsoleKey.D1, false, false, false),
                new ConsoleKeyInfo('~', ConsoleKey.Oem7, false, false, false),
                new ConsoleKeyInfo('a', ConsoleKey.A, false, false, false),
                new ConsoleKeyInfo('b', ConsoleKey.B, false, false, false),
                new ConsoleKeyInfo('c', ConsoleKey.C, false, false, false)
            ];

            chunkedDataProcessor.ProcessChunk(chunk);

            Assert.AreEqual(expected, output);
        }

        [TestCase("a", "a")]
        [TestCase("ab", "a b")]
        [TestCase("a b", "a b")]
        [TestCase("a bc", "a b c")]
        [TestCase("ab c", "a b c")]
        [TestCase("ab <13>c</13>", "a b !c")]
        [TestCase("ab<13>c</13>", "a b !c")]
        [TestCase("a<13>b c</13>", "a !bc")]
        [TestCase("a< 13>bc</13>", "a !bc")]
        [TestCase("a<13>bc</13 >", "a !bc")]
        [TestCase("ab <14>c</14>", "a b !!c")]
        [TestCase("ab<14>c</14>", "a b !!c")]
        [TestCase("a<14>b c</14>", "a !!bc")]
        [TestCase("a< 14>bc</14>", "a !!bc")]
        [TestCase("a<14>bc</14 >", "a !!bc")]
        [TestCase("ab <13>c</13> d", "a b !c d")]
        [TestCase("ab <14>c</14> d", "a b !!c d")]
        [TestCase("ab <13>c</13> <14>d</14>", "a b !c !!d")]
        [TestCase("ab<13>c</13 ><14 >d</14>", "a b !c !!d")]
        [TestCase("ab <13>c</13> <14>d</14> e", "a b !c !!d e")]
        [TestCase("ab <14>c</14> <13>d</13> e", "a b !!c !d e")]
        [TestCase("ab<13>c</13 ><14 >d</14> e", "a b !c !!d e")]
        [TestCase("ab <13>c</13> <14>d</14> <13>e</13>", "a b !c !!d !e")]
        [TestCase("ab<13>c</13 ><14 >d</14> <13>e</13>", "a b !c !!d !e")]
        public void Tags13And14Stories(string input, string expected)
        {
            string result = string.Empty;

            IEnumerable<IMatcher<char>> matchers =
            [
                new StartsEndsWithMatcher<char>(s => OnComplete("!" + s), c => new Rune(c), "<13>", "</13>"),
                new StartsEndsWithMatcher<char>(s => OnComplete("!!" + s), c => new Rune(c), "<14>", "</14>"),
                new GenericMatcher<char>(c => OnComplete(c.ToString()))
            ];

            var chunkedDataProcessor = new InputProcessor<char>(matchers);

            string[] inputs = input.Split();
            foreach (string chunk in inputs) chunkedDataProcessor.ProcessChunk(chunk.ToCharArray());

            Assert.AreEqual(expected, result[1..]);
            return;

            void OnComplete(string newStr)
            {
                result = string.Join(" ", result, newStr);
            }
        }

        [TestCase("\x1b[97;2u", 97, 2, 1, 'u')]
        [TestCase("\x1b[97;65u", 97, 65, 1, 'u')]
        [TestCase("\x1b[97;66u", 97, 66, 1, 'u')]
        [TestCase("\x1b[97u", 97, 1, 1, 'u')]
        [TestCase("\x1b[1;2A", 1, 2, 1, 'A')]
        public void CsiKeyboardMatcherTest(string input, int expectedKey, int expectedModifiers, int expectedEventType, char expectedTerminator)
        {
            (int keyCode, int modifiers, int eventType, char terminator)? matched = null;
            var matcher = new CsiKeyboardMatcher<char>(res => matched = res, c => new Rune(c));
            var processor = new InputProcessor<char>([matcher]);
            processor.ProcessChunk(input.ToCharArray());

            Assert.IsNotNull(matched);
            Assert.AreEqual(expectedKey, matched!.Value.keyCode);
            Assert.AreEqual(expectedModifiers, matched.Value.modifiers);
            Assert.AreEqual(expectedEventType, matched.Value.eventType);
            Assert.AreEqual(expectedTerminator, matched.Value.terminator);
        }
    }
}