using System;
using System.Net;
using System.Net.Sockets;

namespace StayPutVR.Osc
{
    /// <summary>
    /// The one place bytes leave the process: a single UDP socket that sends OSC messages to
    /// StayPutVR's receive port. One-way by design — StayPutVR's shock and bite paths are
    /// fire-and-forget triggers, so nothing is ever read back and no OSCQuery handshake is
    /// needed on this side.
    ///
    /// The socket is deliberately left *unconnected* and every datagram goes out through
    /// <c>SendTo</c>. A connected UDP socket on Windows surfaces the ICMP "port unreachable"
    /// that comes back when nothing is listening as a <c>ConnectionReset</c> on the *next*
    /// send, which would make every trigger fail for one shot after StayPutVR restarts. An
    /// unconnected socket ignores that ICMP, so a trigger sent while StayPutVR is down is
    /// simply dropped by the kernel and the next one still works.
    ///
    /// Sending is done inline on the caller's thread. A loopback datagram is a single syscall
    /// that does not block, so there is no queue and no worker thread to go wrong; the whole
    /// failure surface is one try/catch and a counter.
    /// </summary>
    public static class OscSender
    {
        private static Socket _socket;
        private static IPEndPoint _target;
        private static string _targetKey = "";
        private static string _lastError = "";
        private static float _lastLoggedAt = -1f;
        private static float _lastFailureAt = -1f;
        private static int _sent, _failed;

        public static int Sent => _sent;
        public static int Failed => _failed;
        public static string LastError => _lastError;
        /// <summary>When the most recent failure happened. The overlay shows trouble for a while after this, then stops — a link that has recovered should not keep reading as broken.</summary>
        public static float LastFailureAt => _lastFailureAt;
        /// <summary>Where the datagrams are going, for the log and the HUD.</summary>
        public static string TargetDescription => _target == null ? "<no socket>" : $"{_target.Address}:{_target.Port}";

        /// <summary>
        /// Point the socket at the configured host and port, rebuilding it if either changed.
        /// Called before every send, so editing MelonPreferences.cfg and reloading retargets
        /// the link without a restart. Returns false if the address will not resolve.
        /// </summary>
        public static bool Ensure(string host, int port)
        {
            var key = $"{host}:{port}";
            if (_socket != null && key == _targetKey) return true;

            Close();
            try
            {
                if (port <= 0 || port > 65535) throw new ArgumentOutOfRangeException(nameof(port), $"port {port} is not in 1..65535");
                if (!IPAddress.TryParse(host, out var ip))
                {
                    // A hostname is allowed but has to resolve to something we can send v4 to.
                    var entries = Dns.GetHostAddresses(host);
                    ip = null;
                    foreach (var candidate in entries)
                    {
                        if (candidate.AddressFamily == AddressFamily.InterNetwork) { ip = candidate; break; }
                    }
                    if (ip == null) throw new ArgumentException($"'{host}' has no IPv4 address");
                }

                _socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                _target = new IPEndPoint(ip, port);
                _targetKey = key;
                Core.Log.Msg($"OSC link opened to {TargetDescription} (send only; no OSCQuery).");
                ShockLog.Line($"OSC link opened to {TargetDescription}");
                return true;
            }
            catch (Exception e)
            {
                Close();
                Note($"could not open the OSC socket for {key}: {e.GetType().Name}: {e.Message}");
                return false;
            }
        }

        /// <summary>Send one message. Returns whether the datagram reached the kernel, not whether StayPutVR got it.</summary>
        public static bool Send(byte[] datagram, string what)
        {
            if (_socket == null || _target == null) { _failed++; _lastFailureAt = UnityEngine.Time.unscaledTime; return false; }
            try
            {
                _socket.SendTo(datagram, _target);
                _sent++;
                return true;
            }
            catch (Exception e)
            {
                _failed++;
                Note($"{what} failed: {e.GetType().Name}: {e.Message}");
                return false;
            }
        }

        /// <summary>Record a failure. The timestamp is always updated; the log line is at most one every ten seconds, so a dead link cannot flood the console.</summary>
        private static void Note(string message)
        {
            _lastError = message;
            var now = UnityEngine.Time.unscaledTime;
            _lastFailureAt = now;
            if (_lastLoggedAt >= 0f && now - _lastLoggedAt < 10f) return;
            _lastLoggedAt = now;
            Core.Log.Warning($"OSC: {message}");
            ShockLog.Line($"OSC failure: {message}");
        }

        public static void Close()
        {
            try { _socket?.Dispose(); } catch { }
            _socket = null;
            _target = null;
            _targetKey = "";
        }

        public static string Stats() => $"{_sent} datagram(s) sent, {_failed} failed" + (_lastError.Length == 0 ? "" : $"; last error: {_lastError}");
    }
}
