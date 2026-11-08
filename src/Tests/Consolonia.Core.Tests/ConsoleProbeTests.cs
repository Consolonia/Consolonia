using System;
using System.Runtime.CompilerServices;
using Consolonia.Controls;
using Consolonia.Core.Dummy;
using Consolonia.Core.Infrastructure;
using NUnit.Framework;

namespace Consolonia.Core.Tests
{
    /// <summary>What ConsoleBase makes of the terminal's replies to its startup probes.</summary>
    [TestFixture]
    public class ConsoleProbeTests
    {
        [TestCase("\u001b[4;800;1600t", 200, 50, 8, 16, TestName = "A report gives the cell size")]
        [TestCase("", 200, 50, 8, 16, TestName = "No report falls back")]
        [TestCase("\u001b[4;8x0;1600t", 200, 50, 8, 16, TestName = "A malformed report falls back")]
        // fewer pixels than cells: a zero-sized cell would scale every picture to nothing
        [TestCase("\u001b[4;40;100t", 200, 50, 8, 16, TestName = "A report under a pixel a cell falls back")]
        [TestCase("\u001b[4;800;1600t", 0, 0, 8, 16, TestName = "No columns falls back")]
        public void CellSizeFromReport(string response, int cols, int rows, int expectedWidth, int expectedHeight)
        {
            Assert.That(ConsoleBase.CellSizeFromReport(response, cols, rows),
                Is.EqualTo((expectedWidth, expectedHeight)));
        }

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

        [Test]
        public void ACapabilitiesWithoutACellSizeHasTheDefaultOne()
        {
            IConsoleCapabilities capabilities = new CapabilitiesWithoutCellSize();

            Assert.That((capabilities.CellPixelWidth, capabilities.CellPixelHeight), Is.EqualTo((8, 16)));
        }

        /// <summary>A console written before the cell size was part of the interface.</summary>
        private sealed class CapabilitiesWithoutCellSize : IConsoleCapabilities
        {
            public ConsoleCapabilities Capabilities => ConsoleCapabilities.None;
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
