using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Avalonia.Threading;
using NUnit.Framework;

namespace Consolonia.NUnit
{
    public static class TestHelpers
    {
        /// <summary>
        ///     Assert text pattern(s) are present in the console buffer.
        /// </summary>
        /// <param name="unitTestConsole"></param>
        /// <param name="patterns">Patterns to search for. </param>
        /// <returns></returns>
        public static async Task AssertHasText(this UnitTestConsole unitTestConsole, params string[] patterns)
        {
            await AssertPatterns(unitTestConsole,
                patterns,
                false,
                true,
                (printBuffer, pattern) => $"Text '{pattern}' was not found in the buffer: \r\n" + printBuffer);
        }

        /// <summary>
        ///     Assert text pattern(s) are NOT present in the console buffer.
        /// </summary>
        /// <param name="unitTestConsole"></param>
        /// <param name="patterns">Patterns to search for.</param>
        /// <returns></returns>
        public static async Task AssertHasNoText(this UnitTestConsole unitTestConsole, params string[] patterns)
        {
            await AssertPatterns(unitTestConsole,
                patterns,
                false,
                false,
                (printBuffer, pattern) => $"Text '{pattern}' was found in the buffer: \r\n" + printBuffer);
        }

        /// <summary>
        ///     Wait until a text pattern appears in the console buffer, asserting failure after the timeout.
        ///     Use instead of a plain assertion when the appearance happens asynchronously (e.g. a window
        ///     opening and painting), so slow CI runners do not make the test flaky.
        /// </summary>
        /// <param name="unitTestConsole"></param>
        /// <param name="pattern">Pattern to wait for.</param>
        /// <param name="timeoutMs">Maximum time to wait before asserting.</param>
        /// <returns></returns>
        public static async Task WaitForText(this UnitTestConsole unitTestConsole, string pattern,
            int timeoutMs = 5000)
        {
            await WaitForTextCondition(unitTestConsole, pattern, true, timeoutMs);

            // Produces the standard failure message with the buffer contents on timeout
            await unitTestConsole.AssertHasText(pattern);
        }

        /// <summary>
        ///     Wait until a text pattern disappears from the console buffer, asserting failure after the timeout.
        ///     Use instead of a fixed delay when the disappearance happens asynchronously (e.g. a window closing),
        ///     so slow CI runners do not make the test flaky.
        /// </summary>
        /// <param name="unitTestConsole"></param>
        /// <param name="pattern">Pattern to wait for disappearance of.</param>
        /// <param name="timeoutMs">Maximum time to wait before asserting.</param>
        /// <returns></returns>
        public static async Task WaitForNoText(this UnitTestConsole unitTestConsole, string pattern,
            int timeoutMs = 5000)
        {
            await WaitForTextCondition(unitTestConsole, pattern, false, timeoutMs);

            // Produces the standard failure message with the buffer contents on timeout
            await unitTestConsole.AssertHasNoText(pattern);
        }

        private static async Task WaitForTextCondition(UnitTestConsole unitTestConsole, string pattern,
            bool shouldMatch, int timeoutMs)
        {
            var stopwatch = Stopwatch.StartNew();
            while (stopwatch.ElapsedMilliseconds < timeoutMs)
            {
                bool found = await Dispatcher.UIThread.InvokeAsync(() =>
                    IsMatch(unitTestConsole.PixelBuffer.PrintBuffer(), false, pattern));
                if (found == shouldMatch)
                    return;
                await Task.Delay(50).ConfigureAwait(true);
            }
        }

        /// <summary>
        ///     Assert text pattern(s) as regular expressions are present in the console buffer.
        /// </summary>
        /// <param name="unitTestConsole"></param>
        /// <param name="regexPatterns">Regular expressions to search for.</param>
        /// <returns></returns>
        public static async Task AssertHasMatch(this UnitTestConsole unitTestConsole,
            [StringSyntax(StringSyntaxAttribute.Regex)]
            params string[] regexPatterns)
        {
            await AssertPatterns(unitTestConsole,
                regexPatterns,
                true,
                true,
                (printBuffer, pattern) => $"Regex '{pattern}' was not found in the buffer: \r\n" + printBuffer);
        }

        /// <summary>
        ///     Assert text pattern(s) as regular expressions are NOT present in the console buffer.
        /// </summary>
        /// <param name="unitTestConsole"></param>
        /// <param name="regexPatterns">Regular expressions to search for. </param>
        /// <returns></returns>
        public static async Task AssertHasNoMatch(this UnitTestConsole unitTestConsole,
            [StringSyntax(StringSyntaxAttribute.Regex)]
            params string[] regexPatterns)
        {
            await AssertPatterns(unitTestConsole,
                regexPatterns,
                true,
                false,
                (printBuffer, pattern) => $"Regex '{pattern}' was found in the buffer: \r\n" + printBuffer);
        }

        private static async Task AssertPatterns(UnitTestConsole unitTestConsole, string[] patterns, bool isRegex,
            bool shouldMatch, Func<string, string, string> onError)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                string printBuffer = unitTestConsole.PixelBuffer.PrintBuffer();
                foreach (string pattern in patterns)
                    if (shouldMatch)
                        Assert.IsTrue(IsMatch(printBuffer, isRegex, pattern), onError(printBuffer, pattern));
                    else
                        Assert.IsFalse(IsMatch(printBuffer, isRegex, pattern), onError(printBuffer, pattern));
            });
        }

        private static bool IsMatch(string printBuffer, bool isRegex, string pattern)
        {
            if (isRegex)
            {
                var regex = new Regex(pattern);
                return regex.IsMatch(printBuffer);
            }

            return printBuffer.Contains(pattern, StringComparison.Ordinal);
        }
    }
}