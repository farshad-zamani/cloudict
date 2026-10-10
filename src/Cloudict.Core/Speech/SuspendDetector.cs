using System;
using System.Diagnostics;
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
    /// <para>A short wait that took far longer than requested is the signal. Only the wait itself is
    /// timed — never the work around it — so a slow page read or a busy moment is never mistaken for
    /// sleep.</para>
    ///
    /// <para><b>macOS and Linux need a second clock.</b> On macOS a long wait does not prove the
    /// machine slept: App Nap throttles the timers of an application sitting idle in the background,
    /// and a two-second wait can take a minute. Taken as sleep, that closed and reopened the helper
    /// browser by itself while the user was away — reported from an Intel Mac on macOS 13.7. On these
    /// systems <see cref="Stopwatch"/> stops counting while the machine is asleep (it reads
    /// <c>CLOCK_UPTIME_RAW</c> on macOS and <c>CLOCK_MONOTONIC</c> on Linux; dotnet/runtime#77945)
    /// while the wall clock keeps going, so the gap <em>between the two clocks</em> is time spent
    /// asleep — and only that. A throttled timer moves both clocks alike and is no longer mistaken for
    /// sleep. On Windows <see cref="Stopwatch"/> keeps counting through sleep, so there the wall clock
    /// alone decides, exactly as before; Windows has no App Nap.</para>
    /// </summary>
    public static class SuspendDetector
    {
        /// <summary>
        /// How much longer than requested a wait must take to count as sleep. Far beyond any
        /// scheduling delay, comfortably short of the shortest real sleep.
        /// </summary>
        public static readonly TimeSpan Slack = TimeSpan.FromSeconds(15);

        /// <summary>True when a wait of <paramref name="requested"/> actually took long enough that
        /// the machine must have been suspended during it. The Windows rule.</summary>
        public static bool SleptDuring(TimeSpan requested, TimeSpan measured) =>
            measured - requested > Slack;

        /// <summary>
        /// The macOS and Linux rule: the wall clock ran well past the request, <em>and</em> well past
        /// a clock that does not count sleep. Throttling stretches both; only sleep separates them.
        /// </summary>
        public static bool SleptDuring(TimeSpan requested, TimeSpan wallElapsed, TimeSpan awakeElapsed) =>
            wallElapsed - requested > Slack && wallElapsed - awakeElapsed > Slack;

        /// <summary>
        /// Waits, and reports whether the machine slept during the wait.
        /// </summary>
        /// <param name="utcNow">Replaces the wall clock, for tests.</param>
        /// <param name="awakeClock">Replaces the clock that excludes sleep, for tests. Ignored on Windows.</param>
        public static async Task<bool> DelayAsync(TimeSpan delay, CancellationToken token,
                                                  Func<DateTime> utcNow = null, Func<TimeSpan> awakeClock = null)
        {
            utcNow ??= () => DateTime.UtcNow;
            awakeClock ??= () => Stopwatch.GetElapsedTime(0);

            var wallStart = utcNow();
            var awakeStart = awakeClock();

            await Task.Delay(delay, token).ConfigureAwait(false);

            var wall = utcNow() - wallStart;

            return OperatingSystem.IsWindows()
                ? SleptDuring(delay, wall)
                : SleptDuring(delay, wall, awakeClock() - awakeStart);
        }
    }
}
