using System;
using System.Runtime.CompilerServices;
using Consolonia.Core.Dummy;
using NUnit.Framework;

namespace Consolonia.Core.Tests
{
    /// <summary>What ConsoleBase makes of the terminal's replies to its startup probes.</summary>
    [TestFixture]
    public class ConsoleProbeTests
    {
        [Test]
        public void WithStdinRedirectedAProbeGetsNoReply()
        {
            Assume.That(Console.IsInputRedirected, Is.True, "the test host's stdin is not redirected");

            // Console.KeyAvailable throws on redirected input: the probe must not get that far, nor even
            // write its query. Not constructed, as the constructor's Console.TreatControlCAsInput throws on
            // Windows with stdin redirected (Unix ignores it, and so went on to the probes).
            var console = (ProbingConsole)RuntimeHelpers.GetUninitializedObject(typeof(ProbingConsole));

            Assert.That(console.Request("\u001b[c", 'c', 50), Is.Empty);
        }

#pragma warning disable CA1812 // made by RuntimeHelpers.GetUninitializedObject
        private sealed class ProbingConsole : DummyConsole
#pragma warning restore CA1812
        {
            public string Request(string request, char terminator, int timeoutMs)
            {
                return RequestAnsiResponse(request, terminator, timeoutMs);
            }
        }
    }
}
