using System;
using System.Collections.Generic;
using UnityEngine;

namespace UnityEssentials.Utilities
{
    /// <summary>
    /// Ticks every running <see cref="Timer"/> from one lazily started frame loop, so timers need
    /// neither a MonoBehaviour nor a coroutine. The loop only exists in play mode; EditMode tests
    /// pump <see cref="TickAll"/> directly.
    /// </summary>
    internal static class TimerRunner
    {
        internal static readonly List<Timer> ActiveTimers = new List<Timer>();

        internal static int Generation;
        internal static bool IsLoopRunning;

        private static bool _isTicking;
        private static bool _hasHoles;

        static TimerRunner()
        {
            StaticResetRegistry.Register(Reset);
        }

        internal static void Add(Timer timer)
        {
            if (timer == null || ActiveTimers.Contains(timer))
            {
                return;
            }

            // Appending during a pass is safe: the pass iterates a count snapshot, so the new
            // timer first ticks on the next frame rather than mid-frame.
            ActiveTimers.Add(timer);

            if (IsLoopRunning || !Application.isPlaying)
            {
                return;
            }

            IsLoopRunning = true;
            RunLoop(Generation);
        }

        internal static void Remove(Timer timer)
        {
            var index = ActiveTimers.IndexOf(timer);
            if (index < 0)
            {
                return;
            }

            if (_isTicking)
            {
                // Removing mid-pass would shift every later index onto an already-ticked timer,
                // so the slot is only blanked here and compacted once the pass is over.
                ActiveTimers[index] = null;
                _hasHoles = true;
                return;
            }

            ActiveTimers.RemoveAt(index);
        }

        internal static void TickAll(float deltaTime, float unscaledDeltaTime)
        {
            if (_isTicking)
            {
                return;
            }

            var count = ActiveTimers.Count;
            _isTicking = true;
            try
            {
                for (var i = 0; i < count; i++)
                {
                    // A reset triggered from a handler empties the list outright.
                    if (i >= ActiveTimers.Count)
                    {
                        break;
                    }

                    var timer = ActiveTimers[i];
                    if (timer == null)
                    {
                        continue;
                    }

                    try
                    {
                        timer.Tick(timer.UseUnscaledTime ? unscaledDeltaTime : deltaTime);
                    }
                    catch (Exception exception)
                    {
                        Debug.LogException(exception);
                    }
                }
            }
            finally
            {
                _isTicking = false;
                if (_hasHoles)
                {
                    Compact();
                }
            }
        }

        internal static void Reset()
        {
            ActiveTimers.Clear();
            _isTicking = false;
            _hasHoles = false;
            IsLoopRunning = false;

            // Bumping the generation is what retires any loop still alive from the last session:
            // it checks the counter around every await and returns as soon as it changes.
            Generation++;
        }

        private static void Compact()
        {
            for (var i = ActiveTimers.Count - 1; i >= 0; i--)
            {
                if (ActiveTimers[i] == null)
                {
                    ActiveTimers.RemoveAt(i);
                }
            }

            _hasHoles = false;
        }

        private static async void RunLoop(int generation)
        {
            try
            {
                while (generation == Generation && ActiveTimers.Count > 0)
                {
                    await Awaitable.NextFrameAsync(Application.exitCancellationToken);

                    if (generation != Generation)
                    {
                        return;
                    }

                    TickAll(Time.deltaTime, Time.unscaledDeltaTime);
                }
            }
            catch (OperationCanceledException)
            {
                // Play mode ended: the expected way out, not a failure.
            }
            catch (Exception exception)
            {
                // Nothing awaits this method, so an escaping exception would be unobserved.
                Debug.LogException(exception);
            }
            finally
            {
                if (generation == Generation)
                {
                    IsLoopRunning = false;
                }
            }
        }
    }
}
