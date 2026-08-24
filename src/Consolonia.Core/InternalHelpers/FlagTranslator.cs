using System;

namespace Consolonia.Core.InternalHelpers
{
    public class FlagTranslator<TInput, TOutput>((TInput inFlag, TOutput outFlag)[] mapping)
        where TInput : Enum
        where TOutput : Enum
    {
        public TOutput Translate(TInput input)
        {
            return Translate(input, false);
        }

        public TOutput Translate(TInput input, bool singleValueOnly)
        {
            TOutput output = default;
            foreach ((TInput inFlag, TOutput outFlag) in mapping)
                if (singleValueOnly)
                {
                    if (input.Equals(inFlag))
                        return outFlag;
                }
                else if (input.HasFlag(inFlag))
                {
                    output = (TOutput)(object)((int)(output as object)! | (int)(object)outFlag);
                }

            return output;
        }
    }
}