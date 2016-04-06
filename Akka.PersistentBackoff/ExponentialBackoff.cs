using System;
using Akka.Util;

namespace Akka.PersistentBackoff {
    /// <summary>
    /// Exponential backoff algorithm
    /// </summary>
    public class ExponentialBackoff : IBackoffStrategy
    {
        private readonly TimeSpan _minBackoff;
        private readonly TimeSpan _maxBackoff;
        private readonly double _randomFactor;
        private int _restartCount;

        public ExponentialBackoff(TimeSpan minBackoff, TimeSpan maxBackoff, double randomFactor)
        {
            if (minBackoff <= TimeSpan.Zero) throw new ArgumentException("MinBackoff must be greater than 0");
            if (maxBackoff < minBackoff) throw new ArgumentException("MaxBackoff must be greater than MinBackoff");
            if (randomFactor < 0.0 || randomFactor > 1.0) throw new ArgumentException("RandomFactor must be between 0.0 and 1.0");

            _minBackoff = minBackoff;
            _maxBackoff = maxBackoff;
            _randomFactor = randomFactor;
        }

        public void Reset()
        {
            _restartCount = 0;
        }

        public bool IsStarted()
        {
            return _restartCount > 0;
        }

        public TimeSpan NextDelay()
        {
            var rand = 1.0 + ThreadLocalRandom.Current.NextDouble() * _randomFactor;
            TimeSpan restartDelay;
            if (_restartCount >= 30)
                restartDelay = _maxBackoff; // duration overflow protection (> 100 years)
            else
            {
                var max = Math.Min(_maxBackoff.Ticks, _minBackoff.Ticks * Math.Pow(2, _restartCount)) * rand;
                if (max >= Double.MaxValue) restartDelay = _maxBackoff;
                else restartDelay = new TimeSpan((long)max);
            }
            _restartCount++;
            return restartDelay;
        }
    }
}