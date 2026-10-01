using System;
using System.Threading;
using System.Threading.Tasks;

namespace Cloudict.Speech
{
    /// <summary>
    /// Notices that the machine was asleep or hibernating.
    ///
    /// <para>Nothing in Cloudict is told when the machine sleeps, and on waking every part of it
    /// carries on as if no time had passed — except that it has. The helper Chrome's microphone
    /// stream and its connection to Google are gone, the page may hold a half-finished phrase from
    /// before, and the record of which words were already typed no longer matches it. The first loop
    /// iteration after waking then typed from that mismatch: old words, new words and repeats
    /// interleaved, until the user restarted the application.</para>
    ///
    /// <para>This works on every platform without asking the operating system anything: a short wait
    /// that took far longer than requested can only mean the machine was suspended in the middle of
    /// it. Only the wait itself is timed — never the work around it — so a slow page read or a busy
    /// moment is never mistaken for sleep.</para>
    /// </summary>
    public static class SuspendDetector
    {
        /// <summary>
        /// How much longer than requested a wait must take to count as sleep. Far beyond any
        /// scheduling delay, comfortably short of the shortest real sleep.
        /// </summary>
        public static readonly TimeSpan Slack = TimeSpan.FromSeconds(15);

        /// <summary>True when a wait of <paramref name="requested"/> actually took long enough that
        /// the machine must have been suspended during it.</summary>
        public static bool SleptDuring(TimeSpan requested, TimeSpan measured) =>
            measured - requested > Slack;

        /// <summary>
        /// Waits, and reports whether the machine slept during the wait. The wall clock is used on
        /// purpose: it is the clock that keeps running while the machine is suspended.
        /// </summary>
        public static async Task<bool> DelayAsync(TimeSpan delay, CancellationToken token, Func<DateTime> utcNow = null)
        {
            utcNow ??= () => DateTime.UtcNow;

            var started = utcNow();
            await Task.Delay(delay, token).ConfigureAwait(false);
            return SleptDuring(delay, utcNow() - started);
        }
    }
}
