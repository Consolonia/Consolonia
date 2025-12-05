namespace Consolonia.Core.Helpers.Logging
{
    public static class LogExtensions
    {
        public static string GetAreaName(LogCategory category)
        {
            return "Consolonia." + category;
        }
    }
}