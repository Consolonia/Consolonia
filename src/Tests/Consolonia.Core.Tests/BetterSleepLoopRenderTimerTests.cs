using System;
using System.Threading;
using Consolonia.Core.Infrastructure;
using NUnit.Framework;

namespace Consolonia.Core.Tests
{
    [TestFixture]
    public class BetterSleepLoopRenderTimerTests
    {
        [Test]
        public void TickFiresOnTrigger()
        {
            using var timer = new BetterSleepLoopRenderTimer();
            var tickFired = new ManualResetEventSlim(false);
            TimeSpan receivedElapsed = TimeSpan.Zero;

            timer.Tick += elapsed =>
            {
                receivedElapsed = elapsed;
                tickFired.Set();
            };

            timer.TriggerTick();
            Assert.IsTrue(tickFired.Wait(TimeSpan.FromSeconds(2)), "Tick should fire after TriggerTick");
            Assert.Greater(receivedElapsed, TimeSpan.Zero);
        }

        [Test]
        public void DoesNotTickWithoutTrigger()
        {
            // Without trigger, no tick should fire within 200ms
            using var timer = new BetterSleepLoopRenderTimer();
            var tickCount = 0;

            timer.Tick += _ => Interlocked.Increment(ref tickCount);

            Thread.Sleep(200);
            Assert.AreEqual(0, tickCount, "Timer should not tick without a trigger within the wait period");
        }

        [Test]
        public void MultipleTriggersCauseMultipleTicks()
        {
            using var timer = new BetterSleepLoopRenderTimer();
            var tickCount = 0;
            var lastTick = new ManualResetEventSlim(false);

            timer.Tick += _ =>
            {
                if (Interlocked.Increment(ref tickCount) >= 3)
                    lastTick.Set();
            };

            timer.TriggerTick();
            Thread.Sleep(50);
            timer.TriggerTick();
            Thread.Sleep(50);
            timer.TriggerTick();

            Assert.IsTrue(lastTick.Wait(TimeSpan.FromSeconds(2)), "Should have received at least 3 ticks");
        }

        [Test]
        public void RunsInBackgroundIsTrue()
        {
            using var timer = new BetterSleepLoopRenderTimer();
            Assert.IsTrue(timer.RunsInBackground);
        }

        [Test]
        public void LoopStopsWhenAllSubscribersRemoved()
        {
            using var timer = new BetterSleepLoopRenderTimer();
            var tickFired = new ManualResetEventSlim(false);
            var tickAfterRemove = new ManualResetEventSlim(false);

            Action<TimeSpan> handler = _ => tickFired.Set();
            timer.Tick += handler;

            timer.TriggerTick();
            Assert.IsTrue(tickFired.Wait(TimeSpan.FromSeconds(2)), "Tick should fire initially");

            timer.Tick -= handler;

            // After removing handler, trigger should not cause tick
            Action<TimeSpan> handler2 = _ => tickAfterRemove.Set();
            // Give the loop time to stop
            Thread.Sleep(100);
            timer.TriggerTick();
            Thread.Sleep(100);
            Assert.IsFalse(tickAfterRemove.IsSet, "No tick should fire after removing subscriber");
        }
    }
}
