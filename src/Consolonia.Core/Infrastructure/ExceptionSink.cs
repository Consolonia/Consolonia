using System.Threading;
using Avalonia.Logging;
using Consolonia.Controls;

namespace Consolonia.Core.Infrastructure
// ReSharper disable once ArrangeNamespaceBody
{
    public class ExceptionSink : ILogSink
    {
        public bool IsEnabled(LogEventLevel level, string area)
        {
            return level is LogEventLevel.Error or LogEventLevel.Fatal;
        }

        public void Log(LogEventLevel level, string area, object source, string messageTemplate)
        {
            Log(level, area, source, messageTemplate, []);
        } // ReSharper disable UnusedMember.Global
        public void Log<T0>(LogEventLevel level, string area, object source, string messageTemplate, T0 propertyValue0)
        {
            Log(level, area, source, messageTemplate, [propertyValue0]);
        }

        public void Log<T0, T1>(LogEventLevel level, string area, object source, string messageTemplate,
            T0 propertyValue0,
            T1 propertyValue1)
        {
            Log(level, area, source, messageTemplate, [propertyValue0, propertyValue1]);
        }

        public void Log<T0, T1, T2>(LogEventLevel level, string area, object source, string messageTemplate,
            T0 propertyValue0,
            T1 propertyValue1, T2 propertyValue2)
        {
            Log(level, area, source, messageTemplate, [propertyValue0, propertyValue1, propertyValue2]);
        }

        public void Log(LogEventLevel level, string area, object source, string messageTemplate,
            params object[] propertyValues)
        {
            // Build message: area + template + property values (if any)
            string message = $"{area}: {messageTemplate}";
            if (propertyValues is { Length: > 0 })
            {
                message += " | Values: ";
                for (int i = 0; i < propertyValues.Length; i++) message += $"[{i}]={propertyValues[i]} ";
                message = message.TrimEnd();
            }

            var consoloniaException = new ConsoloniaException(message)
            {
                Source = source?.ToString()
            };

            for (int i = 0; i < propertyValues.Length; i++) consoloniaException.Data.Add(i, propertyValues[i]);

            // debugger does not stop like this: Environment.FailFast(consoloniaException.Message, consoloniaException);
            ThreadPool.QueueUserWorkItem(_ => throw consoloniaException);
        }
    }
    // ReSharper restore UnusedMember.Global
}