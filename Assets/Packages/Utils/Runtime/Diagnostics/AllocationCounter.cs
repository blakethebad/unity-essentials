using System;

namespace UnityEssentials.Utilities
{
    public struct AllocationCounter
    {
        private long _accumulatedBytes;
        private long _spanStartBytes;
        private int _spanStartCollections;
        private bool _isRunning;
        private bool _collectionOccurred;

        /// <summary>Creates a counter that is already measuring.</summary>
        public static AllocationCounter StartNew()
        {
            var counter = default(AllocationCounter);
            counter.Start();
            return counter;
        }

        public long AllocatedBytes
        {
            get
            {
                if (!_isRunning)
                {
                    return _accumulatedBytes;
                }

                var delta = GC.GetTotalMemory(false) - _spanStartBytes;
                return _accumulatedBytes + (delta > 0L ? delta : 0L);
            }
        }

        public bool IsRunning => _isRunning;

        public bool CollectionOccurred => _collectionOccurred;

        /// <summary>Begins or resumes measuring; does nothing when the counter is already running.</summary>
        public void Start()
        {
            if (_isRunning)
            {
                return;
            }

            _spanStartCollections = TotalCollections();
            _spanStartBytes = GC.GetTotalMemory(false);
            _isRunning = true;
        }

        /// <summary>Ends the current span, folding its heap growth into the accumulated total.</summary>
        public void Stop()
        {
            if (!_isRunning)
            {
                return;
            }

            var delta = GC.GetTotalMemory(false) - _spanStartBytes;
            _accumulatedBytes += delta > 0L ? delta : 0L;

            if (TotalCollections() != _spanStartCollections)
            {
                _collectionOccurred = true;
            }

            _isRunning = false;
        }

        /// <summary>Stops the counter and clears both the total and <see cref="CollectionOccurred"/>.</summary>
        public void Reset()
        {
            _accumulatedBytes = 0L;
            _spanStartBytes = 0L;
            _spanStartCollections = 0;
            _isRunning = false;
            _collectionOccurred = false;
        }

        /// <summary>Clears the counter and immediately begins a fresh span.</summary>
        public void Restart()
        {
            Reset();
            Start();
        }

        private static int TotalCollections()
        {
            var total = 0;
            for (var generation = 0; generation <= GC.MaxGeneration; generation++)
            {
                total += GC.CollectionCount(generation);
            }

            return total;
        }
    }
}
