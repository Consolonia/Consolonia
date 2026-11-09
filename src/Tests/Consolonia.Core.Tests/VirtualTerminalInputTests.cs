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
            // The test runner redirects the console, so Enable returns a scope that has nothing to restore.
            // Disposing that scope must still be safe.
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