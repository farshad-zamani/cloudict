using System;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Cloudict.App.Services
{
    /// <summary>Verbs a second launch can send to the instance already running.</summary>
    public enum InstanceCommand
    {
        None,
        Show,
        Toggle,
        Start,
        Stop
    }

    /// <summary>
    /// Keeps one instance running and lets later launches talk to it.
    ///
    /// <para>The 2.x build used a named <c>Mutex</c>, which exists only on Windows and could do
    /// nothing but refuse the second start. This uses a lock file plus a Unix domain socket, both of
    /// which work identically on all three systems, and the socket carries commands.</para>
    ///
    /// <para>That channel is not a convenience. On Wayland no application can register a global
    /// shortcut, so the supported answer is for the user to bind one in their desktop settings that
    /// runs <c>cloudict --toggle</c> — which only means anything if a second launch can reach the
    /// first. It is equally useful elsewhere for scripting and for taskbar launchers.</para>
    /// </summary>
    public static class SingleInstance
    {
        private static FileStream _lock;
        private static Socket _listener;
        private static CancellationTokenSource _cancellation;

        /// <summary>Raised on the running instance when another launch sends a verb.</summary>
        public static event EventHandler<InstanceCommand> CommandReceived;

        public static InstanceCommand ParseCommand(string[] args)
        {
            foreach (var arg in args ?? Array.Empty<string>())
            {
                switch (arg.Trim().ToLowerInvariant())
                {
                    case "--toggle": return InstanceCommand.Toggle;
                    case "--start": return InstanceCommand.Start;
                    case "--stop": return InstanceCommand.Stop;
                    case "--show": return InstanceCommand.Show;
                }
            }

            return InstanceCommand.None;
        }

        private static string RuntimeDirectory
        {
            get
            {
                // XDG_RUNTIME_DIR is the correct home for sockets on Linux; elsewhere fall back to
                // the per-user local data directory, which is writable on every platform.
                var runtime = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
                if (!string.IsNullOrWhiteSpace(runtime) && Directory.Exists(runtime))
                    return Path.Combine(runtime, "cloudict");

                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Cloudict");
            }
        }

        private static string LockPath => Path.Combine(RuntimeDirectory, "instance.lock");
        private static string SocketPath => Path.Combine(RuntimeDirectory, "instance.sock");

        /// <summary>Claims the single-instance slot. False when another instance already holds it.</summary>
        public static bool TryAcquire()
        {
            try
            {
                Directory.CreateDirectory(RuntimeDirectory);

                // An exclusive handle is released by the OS even if the process is killed, so a
                // crash cannot leave the app permanently unable to start.
                _lock = new FileStream(LockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);

                StartListener();
                return true;
            }
            catch (IOException)
            {
                return false;
            }
            catch (Exception ex)
            {
                // Never let this stop the app from running; worst case two instances start.
                Debug.WriteLine($"[SingleInstance] acquire failed: {ex.Message}");
                return true;
            }
        }

        private static void StartListener()
        {
            try
            {
                // A stale socket file survives an unclean exit and would block binding.
                if (File.Exists(SocketPath)) File.Delete(SocketPath);

                _listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                _listener.Bind(new UnixDomainSocketEndPoint(SocketPath));
                _listener.Listen(4);

                _cancellation = new CancellationTokenSource();
                _ = Task.Run(() => AcceptLoopAsync(_cancellation.Token));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SingleInstance] listener unavailable: {ex.Message}");
            }
        }

        private static async Task AcceptLoopAsync(CancellationToken token)
        {
            var buffer = new byte[64];

            while (!token.IsCancellationRequested)
            {
                try
                {
                    using var client = await _listener.AcceptAsync(token);
                    int read = await client.ReceiveAsync(buffer, SocketFlags.None, token);
                    if (read <= 0) continue;

                    var text = Encoding.UTF8.GetString(buffer, 0, read).Trim();
                    if (Enum.TryParse<InstanceCommand>(text, ignoreCase: true, out var command))
                        CommandReceived?.Invoke(null, command);
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[SingleInstance] accept: {ex.Message}");
                    await Task.Delay(200, CancellationToken.None);
                }
            }
        }

        /// <summary>Sends a verb to the running instance. False when nothing is listening.</summary>
        public static bool TrySend(InstanceCommand command)
        {
            if (command == InstanceCommand.None) return false;

            try
            {
                using var client = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                client.Connect(new UnixDomainSocketEndPoint(SocketPath));
                client.Send(Encoding.UTF8.GetBytes(command.ToString()));
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SingleInstance] send '{command}' failed: {ex.Message}");
                return false;
            }
        }

        public static void Release()
        {
            try { _cancellation?.Cancel(); } catch (Exception ex) { Debug.WriteLine(ex.Message); }

            try { _listener?.Dispose(); } catch (Exception ex) { Debug.WriteLine(ex.Message); }
            try { _lock?.Dispose(); } catch (Exception ex) { Debug.WriteLine(ex.Message); }

            try { if (File.Exists(SocketPath)) File.Delete(SocketPath); }
            catch (Exception ex) { Debug.WriteLine($"[SingleInstance] socket cleanup: {ex.Message}"); }

            _listener = null;
            _lock = null;
        }
    }
}
