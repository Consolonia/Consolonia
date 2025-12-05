using System.IO;
using System.Runtime.CompilerServices;
using Avalonia.Logging;

namespace Consolonia.Core.Helpers.Logging
{
    public static class Log
    {
        public static ParametrizedLogger Create(LogCategory category, LogEventLevel level)
        {
            string areaName = LogExtensions.GetAreaName(category);
            ParametrizedLogger? parametrizedLogger = Logger.TryGet(level, areaName);
            return parametrizedLogger != null
                ? (ParametrizedLogger)parametrizedLogger!
                : new ParametrizedLogger(new NullSink(), level, areaName);
        }

        public static ParametrizedLogger CreateInputLogger(LogEventLevel level)
        {
            return Create(LogCategory.Input, level);
        }


        public static void Log2(this ParametrizedLogger logger, string message,
            [CallerFilePath] string sourcePath = null)
        {
            string sourceName = Path.GetFileName(sourcePath ?? "default");
            logger.Log(sourceName, "{Raw}", message); // using template because our message contains placeholders
        }

        public static void Log2<T>(this ParametrizedLogger logger, string messageTemplate, T parameter,
            [CallerFilePath] string sourcePath = null)
        {
            string sourceName = Path.GetFileName(sourcePath ?? "default");
            logger.Log(sourceName, messageTemplate, parameter);
        }

        public static void Log3<T0, T1>(this ParametrizedLogger logger, string messageTemplate, T0 propertyValue0,
            T1 propertyValue1,
            [CallerFilePath] string sourcePath = null)
        {
            string sourceName = Path.GetFileName(sourcePath ?? "default");
            logger.Log(sourceName, messageTemplate, propertyValue0, propertyValue1);
        }

        public static void Log3<T0, T1, T2>(this ParametrizedLogger logger, string messageTemplate, T0 propertyValue0,
            T1 propertyValue1, T2 propertyValue2,
            [CallerFilePath] string sourcePath = null)
        {
            string sourceName = Path.GetFileName(sourcePath ?? "default");
            logger.Log(sourceName, messageTemplate, propertyValue0, propertyValue1, propertyValue2);
        }

        internal class NullSink : ILogSink
        {
            public bool IsEnabled(LogEventLevel level, string area)
            {
                return false;
            }

            public void Log(LogEventLevel level, string area, object source, string messageTemplate)
            {
            }

            public void Log(LogEventLevel level, string area, object source, string messageTemplate,
                params object[] propertyValues)
            {
            }
        }
    }
}