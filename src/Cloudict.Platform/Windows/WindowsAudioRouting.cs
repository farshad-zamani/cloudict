using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Cloudict.Abstractions;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Cloudict.Platform.Windows
{
    /// <summary>
    /// Makes Google Translate hear what Windows is playing.
    ///
    /// <para>Two things have to happen at once, and the second is the one that is easy to get wrong.
    /// Chrome's speech recognition reads from whatever Windows calls the default recording device,
    /// so that has to point at something carrying the system's output. A virtual audio cable
    /// provides such a device — but a cable is a <em>playback</em> device at one end, so simply
    /// sending the system's sound down it would silence the speakers, and the user would be playing
    /// a voice note they cannot hear.</para>
    ///
    /// <para>So Cloudict does the carrying itself: it captures the speakers with WASAPI loopback,
    /// which is passive and leaves playback completely untouched, and plays that capture into the
    /// cable's input. The user hears everything exactly as before, and the cable's output — now the
    /// default recording device — carries the same audio to Chrome.</para>
    ///
    /// <para>Devices that already carry the system's output on their own, such as Stereo Mix or a
    /// Voicemeeter bus, need no bridge; only the default-device switch.</para>
    /// </summary>
    [SupportedOSPlatform("windows")]
    internal sealed class WindowsAudioRouting : IAudioRouting
    {
        /// <summary>
        /// Recording devices that carry the system's output. Ordered by preference: a dedicated
        /// cable first, then a mixer bus, then the sound card's own loopback.
        /// </summary>
        /// <remarks>
        /// Voicemeeter's buses are deliberately not on this list even though they look like
        /// candidates. A Voicemeeter bus carries only what the user has routed to it in Voicemeeter's
        /// own mixer, so Cloudict cannot promise it carries the system's output — it would work on
        /// one machine and silently transcribe nothing on the next. VB-CABLE is listed first because
        /// Cloudict controls both of its ends and can therefore guarantee the result.
        /// </remarks>
        private static readonly string[] CaptureDeviceHints =
        {
            "CABLE Output",   // VB-CABLE: Cloudict feeds one end and Chrome reads the other
            "Stereo Mix",     // built into some sound cards; carries the output natively
            "What U Hear",
            "Wave Out Mix"
        };

        /// <summary>The playback half of a cable, which Cloudict feeds. Empty for devices that need no bridge.</summary>
        private static readonly Dictionary<string, string> BridgeTargets = new(StringComparer.OrdinalIgnoreCase)
        {
            ["CABLE Output"] = "CABLE Input"
        };

        private const string HelperName = "VB-CABLE";
        private const string HelperUrl = "https://vb-audio.com/Cable/";

        private readonly string _restoreFile;
        private readonly object _gate = new();

        private WasapiLoopbackCapture _capture;
        private WasapiOut _bridge;
        private BufferedWaveProvider _buffer;
        private string _previousDefaultCaptureId;

        public WindowsAudioRouting(IAppPaths paths)
        {
            // Deliberately beside the settings rather than in memory: if Cloudict is killed, this
            // file is the only record that the machine's default recording device was changed.
            _restoreFile = Path.Combine(paths.DataDirectory, "audio-restore.txt");
        }

        public bool IsSupported => true;
        public bool IsActive { get; private set; }

        /// <summary>
        /// The loudest sample seen recently, decaying over about a second so a brief gap between
        /// words does not read as silence but a real pause does. -1 when nothing is being bridged,
        /// which is the case for a device that carries the system's output on its own.
        /// </summary>
        public float CurrentLevel
        {
            get
            {
                if (_capture == null) return -1f;

                var age = (DateTime.UtcNow - _peakAt).TotalMilliseconds;
                if (age > 1000) return 0f;

                return (float)(_peak * (1.0 - age / 1000.0));
            }
        }

        private volatile float _peak;
        private DateTime _peakAt = DateTime.UtcNow;

        private static void Log(string message) => DiagnosticLog.Write("WindowsAudioRouting", message);

        #region Probe

        public AudioRoutingStatus Probe()
        {
            try
            {
                var device = FindCaptureDevice();

                if (device == null)
                    return new AudioRoutingStatus
                    {
                        State = AudioRoutingState.HelperMissing,
                        HelperName = HelperName,
                        HelperUrl = HelperUrl,
                        MessageKey = "SystemAudio_HelperMissing"
                    };

                return new AudioRoutingStatus
                {
                    State = IsActive ? AudioRoutingState.Active : AudioRoutingState.Ready,
                    CaptureDevice = device.FriendlyName,
                    HelperName = HelperName,
                    HelperUrl = HelperUrl
                };
            }
            catch (Exception ex)
            {
                Log($"probe failed: {ex.Message}");
                return new AudioRoutingStatus
                {
                    State = AudioRoutingState.HelperMissing,
                    HelperName = HelperName,
                    HelperUrl = HelperUrl,
                    MessageKey = "SystemAudio_HelperMissing"
                };
            }
        }

        /// <summary>The first active recording device whose name says it carries the system's output.</summary>
        private static MMDevice FindCaptureDevice()
        {
            using var enumerator = new MMDeviceEnumerator();
            var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active).ToList();

            foreach (var hint in CaptureDeviceHints)
            {
                var match = devices.FirstOrDefault(
                    d => d.FriendlyName.IndexOf(hint, StringComparison.OrdinalIgnoreCase) >= 0);

                if (match != null) return match;
            }

            return null;
        }

        /// <summary>The playback device this capture device is the other end of, or null when it needs no bridge.</summary>
        private static MMDevice FindBridgeTarget(string captureName)
        {
            var hint = BridgeTargets.FirstOrDefault(
                p => captureName.IndexOf(p.Key, StringComparison.OrdinalIgnoreCase) >= 0).Value;

            if (hint == null) return null;

            using var enumerator = new MMDeviceEnumerator();
            return enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)
                             .FirstOrDefault(d => d.FriendlyName.IndexOf(hint, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        #endregion

        #region Enable / disable

        public AudioRoutingStatus Enable()
        {
            lock (_gate)
            {
                if (IsActive) return Probe();

                var status = Probe();
                if (status.State == AudioRoutingState.HelperMissing) return status;

                try
                {
                    using var enumerator = new MMDeviceEnumerator();
                    var target = FindCaptureDevice();
                    if (target == null) return status;

                    // Written before anything changes, so a kill at any point after this leaves a
                    // trail back to the user's own setting.
                    var current = TryGetDefaultCaptureId(enumerator);

                    // Never record the routing device as the thing to go back to. If the default is
                    // already the cable — a restore that did not take, or a second enable — saving it
                    // here would overwrite the only record of the user's real microphone, and every
                    // restore afterwards would put the cable back. That is how it gets stuck for good.
                    if (!string.IsNullOrEmpty(current) &&
                        !string.Equals(current, target.ID, StringComparison.OrdinalIgnoreCase))
                    {
                        _previousDefaultCaptureId = current;
                        SaveRestorePoint(current);
                    }
                    else
                    {
                        // Fall back to whatever the last good record was, rather than losing it.
                        _previousDefaultCaptureId ??= ReadRestorePoint();
                        Log($"default is already '{target.FriendlyName}'; keeping the existing restore point");
                    }

                    StartBridgeIfNeeded(target.FriendlyName);

                    if (!SetDefaultCaptureDevice(target.ID))
                    {
                        StopBridge();
                        ClearRestorePoint();
                        Log("could not set the default recording device");
                        return new AudioRoutingStatus
                        {
                            State = AudioRoutingState.HelperMissing,
                            HelperName = HelperName,
                            HelperUrl = HelperUrl,
                            MessageKey = "SystemAudio_SwitchFailed"
                        };
                    }

                    IsActive = true;
                    Log($"system audio routed through '{target.FriendlyName}'");

                    return new AudioRoutingStatus
                    {
                        State = AudioRoutingState.Active,
                        CaptureDevice = target.FriendlyName,
                        HelperName = HelperName,
                        HelperUrl = HelperUrl
                    };
                }
                catch (Exception ex)
                {
                    Log($"enable failed: {ex.Message}");
                    Disable();

                    return new AudioRoutingStatus
                    {
                        State = AudioRoutingState.HelperMissing,
                        HelperName = HelperName,
                        HelperUrl = HelperUrl,
                        MessageKey = "SystemAudio_SwitchFailed"
                    };
                }
            }
        }

        public void Disable()
        {
            lock (_gate)
            {
                StopBridge();

                // The file, not the field, is the record that matters. The field is empty whenever
                // this object did not perform the enable itself, and clearing the file on the way
                // out without restoring is what left a machine recording from the cable with no way
                // back except the Sound control panel.
                var target = _previousDefaultCaptureId ?? ReadRestorePoint();

                if (string.IsNullOrEmpty(target))
                {
                    _previousDefaultCaptureId = null;
                    IsActive = false;
                    return;
                }

                if (RestoreAndVerify(target))
                {
                    _previousDefaultCaptureId = null;
                    ClearRestorePoint();
                }
                else
                {
                    // Leave the file alone so the next start tries again.
                    Log("could not restore the default recording device; the restore point is kept");
                }

                IsActive = false;
            }
        }

        /// <summary>
        /// Sets the device and reads it back, retrying once. Windows applies the change
        /// asynchronously, and a call that returned success has occasionally not taken effect by the
        /// time anything looks — so the only honest confirmation is the device itself.
        /// </summary>
        private static bool RestoreAndVerify(string deviceId)
        {
            for (int attempt = 1; attempt <= 2; attempt++)
            {
                SetDefaultCaptureDevice(deviceId);
                System.Threading.Thread.Sleep(250);

                try
                {
                    using var enumerator = new MMDeviceEnumerator();
                    var now = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Console);

                    if (string.Equals(now.ID, deviceId, StringComparison.OrdinalIgnoreCase))
                    {
                        Log($"default recording device restored to '{now.FriendlyName}'");
                        return true;
                    }

                    Log($"restore attempt {attempt} did not take: default is '{now.FriendlyName}'");
                }
                catch (Exception ex)
                {
                    Log($"could not read back the default device: {ex.Message}");
                }
            }

            return false;
        }

        /// <summary>
        /// Carries the speakers into the cable. Loopback capture is passive — it does not take the
        /// audio away from the speakers — so the user goes on hearing whatever they are playing.
        /// </summary>
        private void StartBridgeIfNeeded(string captureDeviceName)
        {
            var sink = FindBridgeTarget(captureDeviceName);
            if (sink == null)
            {
                Log($"'{captureDeviceName}' carries the system's output on its own; no bridge needed");
                return;
            }

            using var enumerator = new MMDeviceEnumerator();
            var speakers = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Console);

            _capture = new WasapiLoopbackCapture(speakers);
            _buffer = new BufferedWaveProvider(_capture.WaveFormat)
            {
                // A short buffer: this has to feel live, and stale audio is worse than a gap.
                BufferDuration = TimeSpan.FromSeconds(2),
                DiscardOnBufferOverflow = true
            };

            _capture.DataAvailable += (_, e) =>
            {
                _buffer.AddSamples(e.Buffer, 0, e.BytesRecorded);
                TrackLevel(e.Buffer, e.BytesRecorded);
            };
            _capture.RecordingStopped += (_, e) =>
            {
                if (e.Exception != null) Log($"loopback capture stopped: {e.Exception.Message}");
            };

            _bridge = new WasapiOut(sink, AudioClientShareMode.Shared, false, 80);
            _bridge.Init(_buffer);

            _capture.StartRecording();
            _bridge.Play();

            Log($"bridging speakers '{speakers.FriendlyName}' into '{sink.FriendlyName}'");
        }

        /// <summary>
        /// Notes how loud this block of audio was. Sampled rather than examined in full: this runs on
        /// the audio callback, where the only job that matters is handing the samples on.
        /// </summary>
        private void TrackLevel(byte[] buffer, int count)
        {
            try
            {
                // The loopback mix format is 32-bit float; anything else is left alone.
                if (_capture?.WaveFormat?.BitsPerSample != 32) return;

                float peak = 0;
                for (int i = 0; i + 3 < count; i += 64)          // every sixteenth sample is plenty
                {
                    var v = Math.Abs(BitConverter.ToSingle(buffer, i));
                    if (v > peak) peak = v;
                }

                if (peak > _peak || (DateTime.UtcNow - _peakAt).TotalMilliseconds > 250)
                {
                    _peak = peak;
                    _peakAt = DateTime.UtcNow;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[WindowsAudioRouting] level: {ex.Message}");
            }
        }

        private void StopBridge()
        {
            try { _bridge?.Stop(); } catch (Exception ex) { Debug.WriteLine(ex.Message); }
            try { _capture?.StopRecording(); } catch (Exception ex) { Debug.WriteLine(ex.Message); }

            try { _bridge?.Dispose(); } catch (Exception ex) { Debug.WriteLine(ex.Message); }
            try { _capture?.Dispose(); } catch (Exception ex) { Debug.WriteLine(ex.Message); }

            _bridge = null;
            _capture = null;
            _buffer = null;
            _peak = 0;
        }

        #endregion

        #region Crash recovery

        public void RecoverInterruptedSession()
        {
            try
            {
                if (!File.Exists(_restoreFile)) return;

                var id = ReadRestorePoint();
                if (string.IsNullOrEmpty(id)) { ClearRestorePoint(); return; }

                Log($"a previous session left the recording device changed; restoring '{id}'");

                // Only forget the record once the device really is back, so a failure here is
                // retried at the next start instead of stranding the user on the cable.
                if (RestoreAndVerify(id)) ClearRestorePoint();
            }
            catch (Exception ex)
            {
                Log($"could not recover the previous session's audio settings: {ex.Message}");
            }
        }

        private void SaveRestorePoint(string deviceId)
        {
            try
            {
                if (string.IsNullOrEmpty(deviceId)) return;

                Directory.CreateDirectory(Path.GetDirectoryName(_restoreFile) ?? ".");
                File.WriteAllText(_restoreFile, deviceId);
            }
            catch (Exception ex)
            {
                Log($"could not write the restore point: {ex.Message}");
            }
        }

        /// <summary>The device the user had before Cloudict touched anything, or null.</summary>
        private string ReadRestorePoint()
        {
            try
            {
                return File.Exists(_restoreFile) ? File.ReadAllText(_restoreFile).Trim() : null;
            }
            catch (Exception ex)
            {
                Log($"could not read the restore point: {ex.Message}");
                return null;
            }
        }

        private void ClearRestorePoint()
        {
            try { if (File.Exists(_restoreFile)) File.Delete(_restoreFile); }
            catch (Exception ex) { Log($"could not clear the restore point: {ex.Message}"); }
        }

        private static string TryGetDefaultCaptureId(MMDeviceEnumerator enumerator)
        {
            try { return enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Console).ID; }
            catch (Exception ex) { Log($"no current default recording device: {ex.Message}"); return null; }
        }

        #endregion

        #region Setting the default device

        /// <summary>
        /// Points Windows at a recording device, for every role.
        ///
        /// <para><c>IPolicyConfig</c> is not in the SDK — Microsoft has never documented a way to do
        /// this — but it is the interface the Sound control panel itself uses and it has been stable
        /// since Windows Vista. Every tool that switches audio devices uses it. Failure is reported
        /// rather than thrown: the user gets told the mode could not be turned on, and nothing is
        /// left half-changed.</para>
        /// </summary>
        private static bool SetDefaultCaptureDevice(string deviceId)
        {
            if (string.IsNullOrEmpty(deviceId)) return false;

            try
            {
                var config = (IPolicyConfig)new PolicyConfigClient();

                // All three roles, so nothing is left pointing at the old device.
                foreach (var role in new[] { ERole.Console, ERole.Multimedia, ERole.Communications })
                {
                    var hr = config.SetDefaultEndpoint(deviceId, role);
                    if (hr != 0)
                    {
                        Log($"SetDefaultEndpoint({role}) returned 0x{hr:X8}");
                        return false;
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                Log($"SetDefaultEndpoint failed: {ex.Message}");
                return false;
            }
        }

        private enum ERole { Console = 0, Multimedia = 1, Communications = 2 }

        [ComImport, Guid("870af99c-171d-4f9e-af0d-e63df40c2bc9")]
        private class PolicyConfigClient { }

        [ComImport, Guid("f8679f50-850a-41cf-9c72-430f290290c8")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IPolicyConfig
        {
            // Only SetDefaultEndpoint is called, but every earlier slot must be declared so the
            // vtable lines up.
            int GetMixFormat(string device, IntPtr format);
            int GetDeviceFormat(string device, bool def, IntPtr format);
            int ResetDeviceFormat(string device);
            int SetDeviceFormat(string device, IntPtr endpointFormat, IntPtr mixFormat);
            int GetProcessingPeriod(string device, bool def, IntPtr defaultPeriod, IntPtr minimumPeriod);
            int SetProcessingPeriod(string device, IntPtr period);
            int GetShareMode(string device, IntPtr mode);
            int SetShareMode(string device, IntPtr mode);
            int GetPropertyValue(string device, bool store, IntPtr key, IntPtr value);
            int SetPropertyValue(string device, bool store, IntPtr key, IntPtr value);
            int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string deviceId, ERole role);
            int SetEndpointVisibility(string device, bool visible);
        }

        #endregion

        public void Dispose()
        {
            try { if (IsActive) Disable(); }
            catch (Exception ex) { Debug.WriteLine($"[WindowsAudioRouting] dispose: {ex.Message}"); }
        }
    }
}
