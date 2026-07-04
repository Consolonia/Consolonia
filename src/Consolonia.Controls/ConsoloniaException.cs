using System;

namespace Consolonia.Controls
{
    //todo: this project seems not needed at all
    public class ConsoloniaException : ApplicationException
    {
        // ReSharper disable UnusedMember.Global
        public ConsoloniaException()

        {
        }

        public ConsoloniaException(string message) : base(message)
        {
        }

        public ConsoloniaException(string message, Exception innerException) : base(message,
            innerException)
        {
        }
        // ReSharper restore UnusedMember.Global
    }
}