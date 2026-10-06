using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Threading;

namespace Consolonia.Core.Helpers
{
    public static class Helper
    {
        public static async Task WaitDispatcherInitialized()
        {
            //todo: check if avalonia exiting to break the loop
            while (AvaloniaLocator.Current.GetService<IDispatcherImpl>() == null) await Task.Yield();
        }

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