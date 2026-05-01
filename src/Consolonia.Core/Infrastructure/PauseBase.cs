using System.Threading.Tasks;

namespace Consolonia.Core.Infrastructure
{
    public class PauseBase
    {
        protected PauseBase()
        {
        }

        private Task PauseTask { get; set; }

        public virtual void PauseIO(Task task)
        {
            task.ContinueWith(_ => { PauseTask = null; }, TaskScheduler.Default);
            PauseTask = task;
        }

        protected Task WaitPauseTaskIfNecessaryAsync()
        {
            Task pauseTask = PauseTask;
            return pauseTask ?? Task.CompletedTask;
        }

        protected void WaitPauseTaskIfNecessary()
        {
            Task pauseTask = PauseTask;
            pauseTask?.Wait();
        }
    }
}