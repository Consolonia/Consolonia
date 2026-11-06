using System.Threading;

namespace Consolonia.Core.Helpers
{
    public static class Helper
    {
        public static void StartOnce(this Timer timer, int ms)
        {
            timer.Change(ms, Timeout.Infinite);
        }

        public static void Stop(this Timer timer)
        {
            timer.Change(Timeout.Infinite, Timeout.Infinite);
        }
    }
}