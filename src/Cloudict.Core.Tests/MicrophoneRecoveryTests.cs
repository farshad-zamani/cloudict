using System;
using System.Threading;
using System.Threading.Tasks;
using Cloudict.Abstractions;
using Cloudict.Speech;
using Xunit;

namespace Cloudict.Core.Tests;

public class MicrophoneRecoveryTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_page_that_stops_mid_phrase_flushes_its_unsent_tail_once_and_keeps_listening(bool live)
    {
        var engine = new Engine();
        var output = new Output();
        var injector = new Injector();
        using var session = Session(engine, output, wordDelay: 1000, injector: injector);
        session.IsLiveTransfer = live;
        Func<string> typed = () => live ? injector.Text : output.FinalText;
        try
        {
            Assert.True(await session.StartAsync());
            engine.Text = "one two three four five six";
            await Until(() => typed().Length > 0);
            engine.Mic = false;
            await Until(() => engine.Resets == 1 && !session.IsResetting);
            Assert.True(session.IsRunning);
            Assert.Equal("one two three four five six", typed());
            engine.Text = "next phrase";
            await Until(() => typed().EndsWith("next phrase", StringComparison.Ordinal));
            Assert.Equal("one two three four five six next phrase", typed());
        }
        finally { await session.StopAsync(); }
    }

    [Fact]
    public async Task The_microphone_is_recovered_even_before_any_words_have_been_spoken()
    {
        var engine = new Engine();
        using var session = Session(engine, new Output());
        try
        {
            await session.StartAsync();
            engine.Mic = false;
            await Until(() => engine.Resets == 1 && !session.IsResetting);
            Assert.True(session.IsRunning);
            Assert.True(engine.Mic);
        }
        finally { await session.StopAsync(); }
    }

    [Theory]
    [InlineData(null)]
    [InlineData(true)]
    public async Task An_unknown_or_live_page_does_not_trigger_a_recovery(bool? state)
    {
        var engine = new Engine();
        using var session = Session(engine, new Output());
        try
        {
            await session.StartAsync();
            engine.Mic = state;
            await Until(() => engine.Checks >= 4);
            Assert.Equal(0, engine.Resets);
            Assert.True(session.IsRunning);
        }
        finally { await session.StopAsync(); }
    }

    [Fact]
    public async Task A_single_off_sample_does_not_restart_the_microphone()
    {
        var engine = new Engine();
        using var session = Session(engine, new Output());
        try
        {
            await session.StartAsync();
            engine.Mic = false;
            await Until(() => engine.Checks >= 1);
            engine.Mic = true;
            await Until(() => engine.Checks >= 4);
            Assert.Equal(0, engine.Resets);
        }
        finally { await session.StopAsync(); }
    }

    [Fact]
    public async Task A_failed_reset_start_is_retried_without_clearing_the_source_again()
    {
        var engine = new Engine();
        using var session = Session(engine, new Output());
        try
        {
            await session.StartAsync();
            engine.FailedStarts = 2;
            engine.Mic = false;
            await Until(() => engine.Starts == 4 && !session.IsResetting);
            Assert.Equal(1, engine.Resets);
            Assert.True(engine.Mic);
            Assert.True(session.IsRunning);
        }
        finally { await session.StopAsync(); }
    }

    [Fact]
    public async Task Exhausted_recovery_requests_one_stop_and_keeps_the_pending_text()
    {
        var engine = new Engine();
        var output = new Output();
        using var session = Session(engine, output, wordDelay: 1000);
        var stopped = 0;
        session.AutoStopped += (_, _) => Interlocked.Increment(ref stopped);
        try
        {
            await session.StartAsync();
            engine.Text = "one two three four five six";
            engine.FailedStarts = 20;
            engine.Mic = false;
            await Until(() => Volatile.Read(ref stopped) == 1);
            await Task.Delay(1200);
            Assert.Equal(1, stopped);
            Assert.Equal(1, engine.Resets);
            Assert.Equal("one two three four five six", output.FinalText);
        }
        finally { await session.StopAsync(); }
    }

    [Fact]
    public async Task Stop_during_recovery_wins_and_no_later_restart_occurs()
    {
        var engine = new Engine { SlowReset = true };
        using var session = Session(engine, new Output());
        await session.StartAsync();
        engine.Mic = false;
        await engine.ResetEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await session.StopAsync();
        var starts = engine.Starts;
        await Task.Delay(1200);
        Assert.False(session.IsRunning);
        Assert.False(engine.Mic);
        Assert.Equal(starts, engine.Starts);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Manual_mode_flushes_the_final_tail_once_without_clearing_or_restarting(bool live)
    {
        var settings = new AppSettings { KeepMicrophoneActive = false, ProcessDelayMs = 50, TransferStartDelayMs = 50,
            WordByWordDelayMs = 1000, InactivityDelayMs = 10000 };
        var engine = new Engine();
        var output = new Output();
        var injector = new Injector();
        using var session = new DictationSession(engine, injector, () => settings, output) { IsLiveTransfer = live };
        var stopped = 0;
        session.AutoStopped += (_, _) => Interlocked.Increment(ref stopped);
        try
        {
            await session.StartAsync();
            engine.Text = "one two three four five six";
            engine.Mic = false;
            await Until(() => Volatile.Read(ref stopped) == 1);
            await Task.Delay(1200);
            Assert.Equal(1, stopped);
            Assert.Equal("one two three four five six", live ? injector.Text : output.FinalText);
            Assert.Equal("one two three four five six", engine.Text);
            Assert.Equal(1, engine.Starts);
            Assert.Equal(0, engine.Resets);
        }
        finally { await session.StopAsync(); }
        Assert.True(await session.StartAsync());
        Assert.Equal(2, engine.Starts);
        await session.StopAsync();
    }

    [Fact]
    public async Task Manual_mode_does_not_reset_on_silence_or_continuous_run_limit()
    {
        var settings = new AppSettings { KeepMicrophoneActive = false, ProcessDelayMs = 50, TransferStartDelayMs = 50,
            WordByWordDelayMs = 50, InactivityDelayMs = 100 };
        var engine = new Engine();
        var output = new Output();
        using var session = new DictationSession(engine, new Injector(), () => settings, output) { MaxContinuousMs = 100 };
        try
        {
            await session.StartAsync();
            engine.Text = "one two";
            await Until(() => output.FinalText == "one two");
            engine.Text = "one two three";
            await Until(() => output.FinalText == "one two three");
            await Task.Delay(1200);
            Assert.True(session.IsRunning);
            Assert.Equal(1, engine.Starts);
            Assert.Equal(0, engine.Resets);
        }
        finally { await session.StopAsync(); }
    }

    [Fact]
    public async Task Disabling_continuous_listening_during_a_browser_reset_releases_capture_and_stops()
    {
        var settings = new AppSettings { ProcessDelayMs = 50, WordByWordDelayMs = 50, InactivityDelayMs = 10000 };
        var engine = new Engine { SlowReset = true };
        using var session = new DictationSession(engine, new Injector(), () => settings, new Output());
        var stopped = 0;
        session.AutoStopped += (_, _) => Interlocked.Increment(ref stopped);
        try
        {
            await session.StartAsync();
            engine.Mic = false;
            await engine.ResetEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            settings.KeepMicrophoneActive = false;
            await Until(() => Volatile.Read(ref stopped) == 1);
            Assert.False(engine.Mic);
            Assert.True(session.IsResetting);
            var starts = engine.Starts;
            await Task.Delay(1200);
            Assert.Equal(starts, engine.Starts);
        }
        finally { await session.StopAsync(); }
    }

    [Fact]
    public async Task Disabling_continuous_listening_during_a_start_retry_releases_capture()
    {
        var settings = new AppSettings { ProcessDelayMs = 50, WordByWordDelayMs = 50, InactivityDelayMs = 10000 };
        var engine = new Engine { SlowRetryStart = true };
        using var session = new DictationSession(engine, new Injector(), () => settings, new Output());
        var stopped = 0;
        session.AutoStopped += (_, _) => Interlocked.Increment(ref stopped);
        try
        {
            await session.StartAsync();
            engine.FailedStarts = 1;
            engine.Mic = false;
            await engine.RetryEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            settings.KeepMicrophoneActive = false;
            await Until(() => Volatile.Read(ref stopped) == 1);
            Assert.False(engine.Mic);
            Assert.Equal(3, engine.Starts);
        }
        finally { await session.StopAsync(); }
    }

    private static DictationSession Session(Engine engine, Output output, int wordDelay = 50, Injector injector = null) =>
        new(engine, injector ?? new Injector(), () => new AppSettings {
            ProcessDelayMs = 50, TransferStartDelayMs = 50, WordByWordDelayMs = wordDelay,
            InactivityDelayMs = 60000
        }, output);

    private static async Task Until(Func<bool> predicate)
    {
        var deadline = DateTime.UtcNow.AddSeconds(12);
        while (!predicate() && DateTime.UtcNow < deadline) await Task.Delay(25);
        Assert.True(predicate(), "Timed out waiting for session behavior");
    }

    private sealed class Engine : ISpeechEngine
    {
        public volatile string Text = "";
        public bool? Mic = true;
        public int Starts, Resets, Checks, FailedStarts;
        public bool SlowReset;
        public bool SlowRetryStart;
        public TaskCompletionSource ResetEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource RetryEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<bool> OpenBrowserAsync(CancellationToken ct = default) => Task.FromResult(true);
        public async Task<bool> StartListeningAsync()
        {
            var starts = Interlocked.Increment(ref Starts);
            if (SlowRetryStart && starts > 2)
            {
                RetryEntered.TrySetResult();
                await Task.Delay(700);
            }
            Mic = Interlocked.Decrement(ref FailedStarts) < 0;
            return Mic == true;
        }
        public Task<bool> StopListeningAsync() { Mic = false; return Task.FromResult(true); }
        public Task<bool?> IsMicrophoneOnAsync() { Interlocked.Increment(ref Checks); return Task.FromResult(Mic); }
        public Task<string> ReadRecognizedTextAsync() => Task.FromResult(Text);
        public Task<string> ClearSourceTextAsync() { Text = ""; return Task.FromResult(""); }
        public async Task<MicResetResult> ResetMicrophoneAsync()
        {
            Interlocked.Increment(ref Resets);
            Text = "";
            Mic = false;
            ResetEntered.TrySetResult();
            if (SlowReset) await Task.Delay(700);
            return new MicResetResult(true, "", await StartListeningAsync());
        }
    }
    private sealed class Output : IDictationOutput
    {
        public string FinalText { get; set; } = "";
        public int CaretIndex { get; set; }
        public void FocusFinalText() { }
    }
    private sealed class Injector : ITextInjector
    {
        public volatile string Text = "";
        public bool IsAvailable => true;
        public string UnavailableReasonKey => null;
        public string BackendName => "test";
        public void TypeText(string text) { Text += text; }
        public void SendKey(InjectedKey key) { }
        public void SendChord(InjectedKey key, KeyModifiers modifiers) { }
        public void Refresh() { }
        public void Dispose() { }
    }
}
