// DUPFINDER_ignore

using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using Consolonia.Core.Drawing.PixelBufferImplementation;
using NUnit.Framework;

namespace Consolonia.Core.Tests.WithLifetimeFixture
{
    [TestFixture]
    public class SimpleSymbolTests
    {
        [Test]
        public void ConstructorChar()
        {
            var symbol = new Symbol('a');
            Assert.That(symbol.Character, Is.EqualTo('a'));
            Assert.That(symbol.Width, Is.EqualTo(1));
        }

        [Test]
        public void ConstructorString()
        {
            var symbol = new Symbol('a');
            Assert.That(symbol.Character, Is.EqualTo('a'));
            Assert.That(symbol.Width, Is.EqualTo(1));
        }

        [Test]
        public void ConstructorRune()
        {
            var symbol = new Symbol(new Rune('a'));
            Assert.That(symbol.Character, Is.EqualTo('a'));
            Assert.That(symbol.Width, Is.EqualTo(1));
        }

        [Test]
        public void ConstructorRuneEmoji()
        {
            Rune rune = "👍".EnumerateRunes().First();
            var symbol = new Symbol(rune);
            Assert.That(symbol.Complex, Is.EqualTo("👍\ufe0f"));
            Assert.That(symbol.Character, Is.EqualTo(char.MinValue));
            Assert.That(symbol.Width, Is.EqualTo(2));
        }

        [Test]
        public void ConstructorCharacterEmoji()
        {
            var symbol = new Symbol("👨‍👩‍👧‍👦");
            Assert.That(symbol.Character, Is.EqualTo(char.MinValue));
            Assert.That(symbol.Complex, Is.EqualTo("👨‍👩‍👧‍👦\ufe0f"));
            Assert.That(symbol.Width, Is.EqualTo(2));
        }

        [Test]
        public void ConstructorCjkChar()
        {
            // CJK ideographs are wide (width=2) but NOT emoji; U+FE0F must NOT be appended
            var symbol = new Symbol('帮'); // U+5E2E, East Asian Width = Wide
            Assert.That(symbol.Character, Is.EqualTo('帮'));
            Assert.That(symbol.Complex, Is.Null);
            Assert.That(symbol.Width, Is.EqualTo(2));
        }

        [Test]
        public void ConstructorCjkString()
        {
            // Same check via the string constructor path
            var symbol = new Symbol("帮");
            Assert.That(symbol.Character, Is.EqualTo('帮'));
            Assert.That(symbol.Complex, Is.Null);
            Assert.That(symbol.Width, Is.EqualTo(2));
        }

        [Test]
        public void Equality()
        {
            var symbol = new Symbol('a');
            var symbol2 = new Symbol('a');
            Assert.That(symbol.Equals((object)symbol2));
            Assert.That(symbol.Equals(symbol2));
            Assert.That(symbol == symbol2);
        }

        [Test]
        public void EqualitySymbol()
        {
            var symbol = new Symbol('a');
            var symbol2 = new Symbol('a');
            Assert.That(symbol.Equals(symbol2));
            Assert.That(symbol.Equals(symbol2));
        }

        [Test]
        public void Inequality()
        {
            var symbol = new Symbol('a');
            var symbol2 = new Symbol('b');
            Assert.That(!symbol.Equals((object)symbol2));
            Assert.That(!symbol.Equals(symbol2));
            Assert.That(symbol != symbol2);
        }

        [Test]
        public void InequalitySymbol()
        {
            var symbol = new Symbol('a');
            var symbol2 = new Symbol('b');
            Assert.That(!symbol.Equals(symbol2));
            Assert.That(!symbol.Equals(symbol2));
        }

        [Test]
        public void HashCode()
        {
            var set = new HashSet<Symbol>();
            set.Add(new Symbol('a'));
            set.Add(new Symbol('a'));
            Assert.That(set.Count, Is.EqualTo(1));

            var set2 = new HashSet<Symbol>();
            set2.Add(new Symbol('a'));
            set2.Add(new Symbol('a'));
            Assert.That(set2.Count, Is.EqualTo(1));
        }

        [Test]
        public void IsWhiteSpace()
        {
            Assert.That(new Symbol(string.Empty).NothingToDraw(), Is.True);
            Assert.That(new Symbol(' ').NothingToDraw(), Is.False);
            Assert.That(new Symbol('a').NothingToDraw(), Is.False);
        }

        [Test]
        public void Blend()
        {
            var symbol = new Symbol('a');
            var symbolAbove = new Symbol('b');
            Symbol newSymbol = symbol.Blend(ref symbolAbove);
            Assert.That(newSymbol.Character, Is.EqualTo('b'));
            Assert.That(newSymbol.Width, Is.EqualTo(1));
        }

        [Test]
        public void BlendWithWhiteSpace()
        {
            var symbol = new Symbol(' ');
            var symbolAbove = new Symbol('b');
            Symbol newSymbol = symbol.Blend(ref symbolAbove);
            Assert.That(newSymbol.Character, Is.EqualTo('b'));
            Assert.That(newSymbol.Width, Is.EqualTo(1));
        }

        [Test]
        public void BlendWithEmpty()
        {
            var symbol = new Symbol('a');
            var symbolAbove = new Symbol(string.Empty);
            Symbol newSymbol = symbol.Blend(ref symbolAbove);
            Assert.That(newSymbol.Character, Is.EqualTo('a'));
            Assert.That(newSymbol.Width, Is.EqualTo(1));
        }

        [Test]
        public void BlendWithEmoji()
        {
            var symbol = new Symbol('a');
            var symbolAbove = new Symbol("👍");
            Symbol newSymbol = symbol.Blend(ref symbolAbove);
            Assert.That(newSymbol.Complex, Is.EqualTo("👍\ufe0f"));
            Assert.That(newSymbol.Width, Is.EqualTo(2));
        }

        [Test]
        public void JsonSerialization()
        {
            var symbol = new Symbol('a');
            string json = JsonSerializer.Serialize(symbol);
            var deserializedSymbol = JsonSerializer.Deserialize<Symbol>(json);
            Assert.That(deserializedSymbol.Equals(symbol));
        }

        [Test]
        [TestCase("👍")]
        [TestCase("👨‍👩‍👧‍👦")]
        [TestCase("\u201c")]
        [TestCase("\u201d")]
        [TestCase("\"")]
        public void JsonSerializationCases(string text)
        {
            var symbol = new Symbol(text);
            string json = JsonSerializer.Serialize(symbol);
            var deserializedSymbol = JsonSerializer.Deserialize<Symbol>(json);
            Assert.That(deserializedSymbol.Equals(symbol));
        }

        [Test]
        public void JsonSerializationDefault()
        {
            var symbol = new Symbol();
            string json = JsonSerializer.Serialize(symbol);
            var deserializedSymbol = JsonSerializer.Deserialize<Symbol>(json);
            Assert.That(deserializedSymbol.Equals(symbol));
        }
    }
}