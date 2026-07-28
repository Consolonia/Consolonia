using Consolonia.Controls;

namespace Consolonia.Core.InternalHelpers
{
    public static class CommonInternalHelper
    {
        public static bool IsNearlyEqual(this double value, double compareTo)
        {
            //todo: strange implementation for this name
            return value.CompareTo(compareTo) == 0;
        }


        public static T NotNull<T>(this T? t) where T : struct
        {
            if (t == null)
                throw new ConsoloniaException("Value is not expected to be null");
            return t.Value;
        }
    }
}