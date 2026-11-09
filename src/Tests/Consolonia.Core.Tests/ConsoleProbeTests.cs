using System;
using System.Runtime.CompilerServices;
using Consolonia.Controls;
using Consolonia.Core.Dummy;
using Consolonia.Core.Infrastructure;
using NUnit.Framework;

namespace Consolonia.Core.Tests
{
    /// <summary>Tests how ConsoleBase reads the terminal's replies to the queries it sends at startup.</summary>
    [TestFixture]
    public class ConsoleProbeTests
    {
        [TestCase("\u001b[4;800;1600t", 200, 50, 8, 16, TestName = "A report gives the cell size")]
        [TestCase("[4;900;1800t", 200, 50, 9, 18, TestName = "A report of another cell size")]
        [TestCase("x[4;900;1800t", 200, 50, 9, 18, TestName = "A keypress before the report is passed over")]
        [TestCase("", 200, 50, 8, 16, TestName = "No report falls back")]
        [TestCase("\u001b[4;8x0;1600t", 200, 50, 8, 16, TestName = "A malformed report falls back")]
        // The report has fewer pixels than cells. A cell size of zero would shrink every picture to nothing.
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

            // Console.KeyAvailable throws when input is redirected. So the probe must return before it
            // reads input, and before it even writes its query.
            // The console is created without running its constructor. The constructor sets
            // Console.TreatControlCAsInput, which throws on Windows when stdin is redirected.
            // (Unix ignores that setting, so on Unix the constructor continued on to the probes.)
            var console = (ProbingConsole)RuntimeHelpers.GetUninitializedObject(typeof(ProbingConsole));

            Assert.That(console.Request("\u001b[c", 'c', 50), Is.Empty);
        }

        [Test]
        public void ACapabilitiesWithoutACellSizeHasTheDefaultOne()
        {
            IConsoleCapabilities capabilities = new CapabilitiesWithoutCellSize();

            Assert.That((capabilities.CellPixelWidth, capabilities.CellPixelHeight), Is.EqualTo((8, 16)));
        }

        /// <summary>
        ///     An older implementation that does not provide a cell size. It relies on the interface's
        ///     default cell size.
        /// </summary>
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