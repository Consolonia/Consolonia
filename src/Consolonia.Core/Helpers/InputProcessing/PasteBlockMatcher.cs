using System;
using System.Text;

namespace Consolonia.Core.Helpers.InputProcessing
{
    public class PasteBlockMatcher<T>(Action<string> onComplete, Func<T, Rune> toRune)
#pragma warning disable CS0618 // Type or member is obsolete: This usage is valid - was tested
        : StartsEndsWithMatcher<T>(onComplete, toRune, @"[200~", @"[201~");
#pragma warning restore CS0618 // Type or member is obsolete
}