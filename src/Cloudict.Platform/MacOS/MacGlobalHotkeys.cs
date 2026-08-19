using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.Versioning;
using System.Threading;
using Cloudict.Abstractions;

namespace Cloudict.Platform.MacOS
{
    /// <summary>
    /// System-wide shortcuts on macOS via Carbon's <c>RegisterEventHotKey</c>.
    ///
    /// <para>Carbon is long deprecated, yet this is still the supported way for an application to
    /// claim a global shortcut without becoming an event tap. It is also the cheaper choice for the
    /// user: an event tap would require Accessibility permission and see every keystroke on the
    /// system, whereas a registered hot key needs no permission at all and only ever reports the one
    /// combination. Typing still needs Accessibility — that is
    /// <see cref="MacTextInjector"/>'s concern — but the shortcut itself works before it is granted,
    /// which matters because the shortcut is how the user starts the app in the first place.</para>
    ///
    /// <para>Carbon delivers to an event target that must be pumped, so a dedicated thread runs the
    /// loop rather than depending on a UI framework's own.</para>
    /// </summary>
    [SupportedOSPlatform("macos")]
    internal sealed class MacGlobalHotkeys : IGlobalHotkeys
    {
        /// <summary>Four-character signature identifying this app's hot keys ('CLDT').</summary>
        private const uint Signature = 0x434C4454;

        private readonly object _gate = new object();
        private readonly Dictionary<uint, Action> _callbacks = new Dictionary<uint, Action>();
        private readonly List<IntPtr> _hotKeyRefs = new List<IntPtr>();

        private Thread _pump;
        private IntPtr _handlerRef;
        private uint _nextId = 1;
        private volatile bool _disposed;

        // Kept alive explicitly: Carbon stores a raw function pointer to this delegate.
        private MacInterop.EventHandlerProcPtr _handler;

        public bool IsSupported => true;
        public string UnsupportedReasonKey => null;

        /// <summary>Not needed: Carbon hot keys register against the application event target.</summary>
        public void Attach(IntPtr nativeWindowHandle) { }

        public bool Register(HotkeyBinding binding, Action onPressed)
        {
            if (_disposed || binding == null || !binding.IsValid || onPressed == null) return false;

            ushort vk = MacVirtualKeys.ToVirtualKey(binding.Key);
            if (vk == MacVirtualKeys.None) return false;

            uint modifiers = 0;
            if (binding.Modifiers.HasFlag(KeyModifiers.Control)) modifiers |= MacInterop.controlKey;
            if (binding.Modifiers.HasFlag(KeyModifiers.Alt)) modifiers |= MacInterop.optionKey;
            if (binding.Modifiers.HasFlag(KeyModifiers.Shift)) modifiers |= MacInterop.shiftKey;
            if (binding.Modifiers.HasFlag(KeyModifiers.Meta)) modifiers |= MacInterop.cmdKey;

            lock (_gate)
            {
                EnsureHandlerInstalled();

                uint id = _nextId++;
                var hotKeyId = new MacInterop.EventHotKeyID { signature = Signature, id = id };

                int status = MacInterop.RegisterEventHotKey(
                    vk, modifiers, hotKeyId, MacInterop.GetEventDispatcherTarget(), 0, out IntPtr hotKeyRef);

                if (status != 0 || hotKeyRef == IntPtr.Zero)
                {
                    Debug.WriteLine($"[MacGlobalHotkeys] {binding} rejected (status {status}) — most likely already claimed");
                    return false;
                }

                _callbacks[id] = onPressed;
                _hotKeyRefs.Add(hotKeyRef);
                return true;
            }
        }

        private void EnsureHandlerInstalled()
        {
            if (_handlerRef != IntPtr.Zero) return;

            _handler = OnHotKeyEvent;

            var spec = new[]
            {
                new MacInterop.EventTypeSpec
                {
                    eventClass = MacInterop.kEventClassKeyboard,
                    eventKind = MacInterop.kEventHotKeyPressed
                }
            };

            int status = MacInterop.InstallEventHandler(
                MacInterop.GetEventDispatcherTarget(), _handler, spec.Length, spec, IntPtr.Zero, out _handlerRef);

            if (status != 0)
            {
                Debug.WriteLine($"[MacGlobalHotkeys] InstallEventHandler failed ({status})");
                return;
            }

            EnsurePump();
        }

        private int OnHotKeyEvent(IntPtr callRef, IntPtr eventRef, IntPtr userData)
        {
            try
            {
                int status = MacInterop.GetEventParameter(
                    eventRef, MacInterop.kEventParamDirectObject, MacInterop.typeEventHotKeyID,
                    IntPtr.Zero, System.Runtime.InteropServices.Marshal.SizeOf<MacInterop.EventHotKeyID>(),
                    IntPtr.Zero, out MacInterop.EventHotKeyID hotKeyId);

                if (status != 0 || hotKeyId.signature != Signature) return 0;

                Action handler;
                lock (_gate) _callbacks.TryGetValue(hotKeyId.id, out handler);

                if (handler != null)
                {
                    // Run off the Carbon pump so a slow handler cannot stall later shortcuts.
                    ThreadPool.QueueUserWorkItem(_ =>
                    {
                        try { handler(); }
                        catch (Exception ex) { Debug.WriteLine($"[MacGlobalHotkeys] handler threw: {ex.Message}"); }
                    });
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MacGlobalHotkeys] event dispatch failed: {ex.Message}");
            }

            return 0;   // noErr — let the event continue on its way
        }

        /// <summary>
        /// Pumps the Carbon event target. <c>ReceiveNextEvent</c> with a short timeout keeps the
        /// thread responsive to shutdown instead of blocking indefinitely.
        /// </summary>
        private void EnsurePump()
        {
            if (_pump != null) return;

            _pump = new Thread(() =>
            {
                while (!_disposed)
                {
                    try
                    {
                        int status = MacInterop.ReceiveNextEvent(0, IntPtr.Zero, 0.25, true, out IntPtr evt);
                        if (status != 0 || evt == IntPtr.Zero) continue;

                        MacInterop.SendEventToEventTarget(evt, MacInterop.GetEventDispatcherTarget());
                        MacInterop.ReleaseEvent(evt);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[MacGlobalHotkeys] pump error: {ex.Message}");
                        Thread.Sleep(250);
                    }
                }
            })
            {
                IsBackground = true,
                Name = "Cloudict global hotkeys (Carbon)"
            };

            _pump.Start();
        }

        public void UnregisterAll()
        {
            lock (_gate)
            {
                foreach (var reference in _hotKeyRefs)
                {
                    try { MacInterop.UnregisterEventHotKey(reference); }
                    catch (Exception ex) { Debug.WriteLine($"[MacGlobalHotkeys] unregister failed: {ex.Message}"); }
                }

                _hotKeyRefs.Clear();
                _callbacks.Clear();
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            try
            {
                UnregisterAll();

                if (_handlerRef != IntPtr.Zero)
                {
                    MacInterop.RemoveEventHandler(_handlerRef);
                    _handlerRef = IntPtr.Zero;
                }

                _pump?.Join(TimeSpan.FromSeconds(1));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MacGlobalHotkeys] dispose: {ex.Message}");
            }
        }
    }
}
