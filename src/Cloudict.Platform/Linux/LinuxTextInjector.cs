using System;
using System.Diagnostics;
using Cloudict.Abstractions;

namespace Cloudict.Platform.Linux
{
    /// <summary>
    /// Chooses how to type on Linux, because there is no single answer.
    ///
    /// <para>On an X11 session, XTEST is the right tool: it needs no privileges, no daemon and no
    /// installed helper. On Wayland it is the wrong tool — events sent through XWayland reach only
    /// X11 clients, so the app would appear to work while typing into nothing in every native
    /// Wayland window. There the only route is <c>uinput</c>, via <c>ydotool</c>.</para>
    ///
    /// <para>The selection is made once, honestly, and the reason is reported when neither route is
    /// usable, so the UI can tell the user what to install rather than leaving them to discover that
    /// dictation produces no text.</para>
    /// </summary>
    internal sealed class LinuxTextInjector : ITextInjector
    {
        private ITextInjector _backend;
        private readonly LinuxDisplayServer _session;

        public LinuxTextInjector()
        {
            _session = LinuxSession.Detect();
            Refresh();
        }

        /// <summary>Which backend was chosen — useful in diagnostics and the capability panel.</summary>
        public string BackendName { get; private set; } = "none";

        public bool IsAvailable => _backend?.IsAvailable == true;

        public string UnavailableReasonKey { get; private set; }

        public void Refresh()
        {
            _backend?.Dispose();
            _backend = null;

            if (_session == LinuxDisplayServer.Wayland)
            {
                var ydotool = new YdotoolTextInjector();
                if (ydotool.IsAvailable)
                {
                    _backend = ydotool;
                    BackendName = "ydotool";
                    UnavailableReasonKey = null;
                    return;
                }

                ydotool.Dispose();

                // XWayland can still serve X11 windows, so it is better than nothing — but say so,
                // because native Wayland windows will not receive anything.
                if (LinuxSession.HasReachableXServer())
                {
                    var partial = new X11TextInjector();
                    if (partial.IsAvailable)
                    {
                        _backend = partial;
                        BackendName = "xtest-via-xwayland";
                        UnavailableReasonKey = "Platform_Warn_LinuxWaylandPartial";
                        return;
                    }

                    partial.Dispose();
                }

                BackendName = "none";
                UnavailableReasonKey = "Platform_Err_LinuxWaylandNeedsYdotool";
                return;
            }

            if (_session == LinuxDisplayServer.X11 || LinuxSession.HasReachableXServer())
            {
                var x11 = new X11TextInjector();
                if (x11.IsAvailable)
                {
                    _backend = x11;
                    BackendName = "xtest";
                    UnavailableReasonKey = null;
                    return;
                }

                UnavailableReasonKey = x11.UnavailableReasonKey;
                x11.Dispose();
                BackendName = "none";
                return;
            }

            BackendName = "none";
            UnavailableReasonKey = "Platform_Err_LinuxNoGraphicalSession";
        }

        public void TypeText(string text)
        {
            try { _backend?.TypeText(text); }
            catch (Exception ex) { Debug.WriteLine($"[LinuxTextInjector] type failed: {ex.Message}"); }
        }

        public void SendKey(InjectedKey key)
        {
            try { _backend?.SendKey(key); }
            catch (Exception ex) { Debug.WriteLine($"[LinuxTextInjector] key failed: {ex.Message}"); }
        }

        public void SendChord(InjectedKey key, KeyModifiers modifiers)
        {
            try { _backend?.SendChord(key, modifiers); }
            catch (Exception ex) { Debug.WriteLine($"[LinuxTextInjector] chord failed: {ex.Message}"); }
        }

        public void Dispose()
        {
            _backend?.Dispose();
            _backend = null;
        }
    }
}
