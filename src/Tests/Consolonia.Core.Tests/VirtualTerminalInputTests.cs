using Consolonia.Core.Infrastructure;
using NUnit.Framework;

namespace Consolonia.Core.Tests
{
    [TestFixture]
    public class VirtualTerminalInputTests
    {
        [Test]
        public void EnableAndDisposeNeverThrow()
        {
            // the test runner's redirected console yields a no-restore scope; it must still be safe to dispose
            Assert.DoesNotThrow(() =>
            {
                using (VirtualTerminalInput.Enable())
                {
                }
            });
        }

        [Test]
        public void DefaultScopeDisposeIsSafeAndIdempotent()
        {
            VirtualTerminalInput.Scope scope = default;

            Assert.DoesNotThrow(() =>
            {
                scope.Dispose();
                scope.Dispose();
            });
        }

        [Test]
        public void DisposingScopeTwiceIsSafe()
        {
            VirtualTerminalInput.Scope scope = VirtualTerminalInput.Enable();

            Assert.DoesNotThrow(() =>
            {
                scope.Dispose();
                scope.Dispose();
            });
        }
    }
}
