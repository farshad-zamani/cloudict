using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Cloudict.Abstractions;
using Cloudict.Speech;
using Xunit;

namespace Cloudict.Core.Tests
{
    /// <summary>
    /// From the macOS report: "after an idle spell the helper browser closed and reopened by itself",
    /// and "sometimes stop did not stop — dictation started again and typed".
    /// </summary>
    public class StopAndIdleTests
    {
        // ---------------------------------------------------------------- sleep vs App Nap

        [Fact]
        public void A_throttled_timer_is_not_sleep_on_macOS_and_Linux()
        {
            // App Nap held a 2 s wait back for a minute. Both clocks moved alike.
            var requested = TimeSpan.FromSeconds(2);
            Assert.False(SuspendDetector.SleptDuring(requested, TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(60)));
        }

        [Fact]
        public void Real_sleep_is_still_sleep_on_macOS_and_Linux()
        {
            // An hour on the wall clock, two seconds awake.
            var requested = TimeSpan.FromSeconds(2);
            Assert.True(SuspendDetector.SleptDuring(requested, TimeSpan.FromHours(1), TimeSpan.FromSeconds(2)));
        }

        [Fact]
        public void A_short_sleep_inside_a_throttled_wait_is_below_the_threshold()
        {
            var requested = TimeSpan.FromSeconds(2);
            Assert.False(SuspendDetector.SleptDuring(requested, TimeSpan.FromSeconds(40), TimeSpan.FromSeconds(30)));
        }

        [Fact]
        public async Task The_awake_clock_is_consulted_away_from_Windows()
        {
            var wall = DateTime.UtcNow;

            // Wall and awake clocks both move exactly a minute between their two reads: throttling,
            // not sleep.
            var wallReads = 0;
            var awakeReads = 0;
            var slept = await SuspendDetector.DelayAsync(TimeSpan.FromMilliseconds(10), CancellationToken.None,
                utcNow: () => wallReads++ == 0 ? wall : wall.AddMinutes(1),
                awakeClock: () => awakeReads++ == 0 ? TimeSpan.Zero : TimeSpan.FromMinutes(1));

            // On Windows only the wall clock counts, and a minute is sleep; elsewhere it is not.
            Assert.Equal(OperatingSystem.IsWindows(), slept);
        }

        // ---------------------------------------------------------------- stop during a reset

        /// <summary>
        /// A stop pressed while a pause-reset was switching the microphone off and on again could
        /// land in the middle, and the reset's "on" came after the user's "off". The microphone must
        /// end up off, and nothing may be typed after the stop.
        /// </summary>
        [Fact]
        public async Task A_stop_during_a_reset_leaves_the_microphone_off()
        {
            var engine = new SlowResetEngine();
            var injector = new CountingInjector();
            var settings = new AppSettings { ProcessDelayMs = 50, WordByWordDelayMs = 50, TransferStartDelayMs = 100, InactivityDelayMs = 300 };
            var session = new DictationSession(engine, injector, () => settings, new NullOutput()) { IsLiveTransfer = true };

            Assert.True(await session.StartAsync());
            engine.Speak("hello world");

            Assert.True(await engine.ResetBegan.Task.WaitAsync(TimeSpan.FromSeconds(10)), "a pause-reset should have begun");

            var typedAtStop = injector.Count;
            await session.StopAsync();
            await Task.Delay(400);

            Assert.Equal("stop", engine.LastMicOperation);
            Assert.False(engine.MicOn);
            Assert.Equal(typedAtStop, injector.Count);
        }

        private sealed class SlowResetEngine : ISpeechEngine
        {
            private readonly object _gate = new object();
            private string _page = string.Empty;

            public TaskCompletionSource<bool> ResetBegan { get; } = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            public string LastMicOperation { get; private set; }
            public bool MicOn { get; private set; }

            public void Speak(string text) { lock (_gate) _page = text; }

            public Task<bool> OpenBrowserAsync(CancellationToken ct = default) => Task.FromResult(true);

            public Task<bool> StartListeningAsync()
            {
                lock (_gate) { MicOn = true; LastMicOperation = "start"; }
                return Task.FromResult(true);
            }

            public Task<bool> StopListeningAsync()
            {
                lock (_gate) { MicOn = false; LastMicOperation = "stop"; }
                return Task.FromResult(true);
            }

            public Task<string> ReadRecognizedTextAsync() { lock (_gate) return Task.FromResult(_page); }

            public Task<string> ClearSourceTextAsync() { lock (_gate) { _page = string.Empty; return Task.FromResult(string.Empty); } }

            public async Task<MicResetResult> ResetMicrophoneAsync()
            {
                lock (_gate) { MicOn = false; LastMicOperation = "reset-stop"; _page = string.Empty; }
                ResetBegan.TrySetResult(true);

                // The page takes its time, as the real one does.
                await Task.Delay(800);

                lock (_gate) { MicOn = true; LastMicOperation = "reset-start"; }
                return new MicResetResult(true, string.Empty, true);
            }
        }

        private sealed class CountingInjector : ITextInjector
        {
            private int _count;
            public int Count => Volatile.Read(ref _count);
            public bool IsAvailable => true;
            public string UnavailableReasonKey => null;
            public string BackendName => "test";
            public void TypeText(string text) => Interlocked.Increment(ref _count);
            public void SendKey(InjectedKey key) { }
            public void SendChord(InjectedKey key, KeyModifiers modifiers) { }
            public void Refresh() { }
            public void Dispose() { }
        }

        private sealed class NullOutput : IDictationOutput
        {
            public string FinalText { get; set; } = string.Empty;
            public int CaretIndex { get; set; }
            public void FocusFinalText() { }
        }
    }
}
