using System;

namespace Akka.PersistentBackoff {
    /// <summary>
    /// Interface description for a backoff strategy
    /// </summary>
    public interface IBackoffStrategy {
        /// <summary>
        /// Reset the backoff algorithm
        /// </summary>
        void Reset();

        /// <summary>
        /// Is the backoff started and are we in a backoff mode
        /// </summary>
        /// <returns></returns>
        bool IsStarted();

        /// <summary>
        /// Returns the next delay
        /// </summary>
        /// <returns></returns>
        TimeSpan NextDelay();
    }
}