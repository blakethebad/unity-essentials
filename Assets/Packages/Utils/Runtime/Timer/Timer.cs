using System;
using UnityEngine;

namespace UnityEssentials.Utilities
{
    public enum TimerMode
    {
        Stopwatch,
        Countdown
    }

    /// <summary>
    /// A stopwatch or countdown driven by <see cref="TimerRunner"/>'s frame loop in play mode.
    /// Stop pauses (Start resumes), Reset zeroes, Restart does both.
    /// A looping countdown raises <see cref="Completed"/> every lap and never latches IsCompleted.
    /// </summary>
    public sealed class Timer
    {
        private readonly TimerMode _mode;
        private readonly bool _useUnscaledTime;
        private readonly bool _loop;
        private readonly float _duration;

        private double _elapsedSeconds;
        private bool _isRunning;
        private bool _isCompleted;

        /// <summary>Creates a stopwatch that counts up from zero until it is stopped or reset.</summary>
        public Timer(bool useUnscaledTime = false)
        {
            _mode = TimerMode.Stopwatch;
            _useUnscaledTime = useUnscaledTime;
        }

        /// <summary>
        /// Creates a countdown that raises <see cref="Completed"/> after <paramref name="durationSeconds"/>,
        /// repeating every lap when <paramref name="loop"/> is true.
        /// </summary>
        public Timer(float durationSeconds, bool loop = false, bool useUnscaledTime = false)
        {
            if (float.IsNaN(durationSeconds) || float.IsInfinity(durationSeconds) || durationSeconds <= 0f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(durationSeconds),
                    durationSeconds,
                    "A countdown duration must be a finite value greater than zero.");
            }

            _mode = TimerMode.Countdown;
            _duration = durationSeconds;
            _loop = loop;
            _useUnscaledTime = useUnscaledTime;
        }

        public event Action Completed;

        public TimerMode Mode => _mode;

        public bool UseUnscaledTime => _useUnscaledTime;

        public bool IsRunning => _isRunning;

        public bool IsCompleted => _isCompleted;

        public float Duration => _duration;

        public float Remaining
        {
            get
            {
                if (_mode == TimerMode.Stopwatch)
                {
                    return 0f;
                }

                var remaining = _duration - (float)_elapsedSeconds;
                return remaining > 0f ? remaining : 0f;
            }
        }

        public float Progress => _mode == TimerMode.Stopwatch
            ? 0f
            : Mathf.Clamp01((float)(_elapsedSeconds / _duration));

        public TimeSpan Elapsed => TimeSpan.FromSeconds(_elapsedSeconds);

        public double ElapsedSeconds => _elapsedSeconds;

        public double ElapsedMilliseconds => _elapsedSeconds * 1000d;

        public double ElapsedMinutes => _elapsedSeconds / 60d;

        public double ElapsedHours => _elapsedSeconds / 3600d;

        /// <summary>Starts or resumes the timer. A finished countdown stays finished — use <see cref="Restart"/>.</summary>
        public void Start()
        {
            StaticResetRegistry.AssertMainThread();

            if (_isRunning || _isCompleted)
            {
                return;
            }

            _isRunning = true;

            // Outside play mode there is no frame loop to join; tests drive Tick themselves.
            if (Application.isPlaying)
            {
                TimerRunner.Add(this);
            }
        }

        /// <summary>Pauses the timer, keeping the elapsed time so <see cref="Start"/> resumes from it.</summary>
        public void Stop()
        {
            StaticResetRegistry.AssertMainThread();

            if (!_isRunning)
            {
                return;
            }

            _isRunning = false;
            TimerRunner.Remove(this);
        }

        /// <summary>Stops the timer and returns it to zero, clearing the completed state.</summary>
        public void Reset()
        {
            StaticResetRegistry.AssertMainThread();

            _isRunning = false;
            _isCompleted = false;
            _elapsedSeconds = 0d;
            TimerRunner.Remove(this);
        }

        /// <summary>Resets the timer to zero and starts it again.</summary>
        public void Restart()
        {
            Reset();
            Start();
        }

        internal void Tick(float deltaTime)
        {
            // A non-positive delta must never rewind the timer; a paused frame is simply skipped.
            if (!_isRunning || deltaTime <= 0f)
            {
                return;
            }

            _elapsedSeconds += deltaTime;

            if (_mode == TimerMode.Stopwatch)
            {
                return;
            }

            while (_elapsedSeconds >= _duration)
            {
                if (!_loop)
                {
                    _elapsedSeconds = _duration;
                    _isRunning = false;
                    _isCompleted = true;
                    TimerRunner.Remove(this);
                    RaiseCompleted();
                    return;
                }

                _elapsedSeconds -= _duration;
                RaiseCompleted();

                // The handler may have stopped, reset or restarted the timer; honour that
                // immediately instead of grinding out the rest of a delta it no longer owns.
                if (!_isRunning)
                {
                    return;
                }
            }
        }

        private void RaiseCompleted()
        {
            var handler = Completed;
            if (handler == null)
            {
                return;
            }

            try
            {
                handler.Invoke();
            }
            catch (Exception exception)
            {
                // State is already consistent at this point, so a faulty subscriber only costs
                // itself: the timer keeps looping and the runner keeps ticking everything else.
                Debug.LogException(exception);
            }
        }
    }
}
