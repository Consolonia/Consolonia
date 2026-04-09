using System;
using System.Text;

namespace Consolonia.Core.Helpers.InputProcessing
{
    /// <summary>
    ///     A matcher that matches any text input.
    /// </summary>
    public class TextInputMatcher<T>(Action<(string, T[])> onComplete, Func<T, Rune> toRune, uint? min = null)
        : RegexMatcher<T>(onComplete, toRune, @"\A[^\x00\x0A\x0D\x1B]+\z")
    {
        public override bool TryFlush()
        {
            if (min != null && GetAccumulatedLength() < (uint)min)
                return false;

            return base.TryFlush();
        }
    }
}