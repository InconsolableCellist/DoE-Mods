using System;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;

namespace StayPutVR.Osc
{
    /// <summary>
    /// Finds the StayPutVR app's receive port over OSC Query, so nobody has to copy a port
    /// number between the two. With OSC Query on — its default — the app binds whatever port is
    /// free and advertises it over mDNS as <c>StayPutVR._osc._udp.local.</c>; this asks for
    /// that name and reads the port out of the answer. The <c>Port</c> setting is only used
    /// while nothing answers: OSC Query off in the app, an app older than 1.5.2, or the app not
    /// running yet.
    ///
    /// The ask is a "legacy unicast" query (RFC 6762 §6.7): one datagram to the mDNS group from
    /// an ordinary socket on an ephemeral port, and the app answers straight back to that port.
    /// So this never binds 5353, never joins the multicast group, and opens no inbound listener
    /// for the firewall to ask about — the reply is just the response to a send. The app does
    /// not listen on loopback (it advertises on the LAN interfaces for VRChat's sake), so the
    /// query goes out on every interface with an IPv4 address; the app hears it on whichever
    /// it shares with this machine and the same-host multicast loops back.
    ///
    /// A background thread does the asking: every couple of seconds until the app is found,
    /// then every ten or so to notice a restart, since a restarted app is on a new port and a
    /// UDP send to the old one fails silently. The thread only writes fields; the main thread
    /// reads them in <see cref="Pump"/> and does the logging, so nothing here touches Unity or
    /// the log from off the main thread.
    /// </summary>
    public static class Discovery
    {
        public const string Instance = "StayPutVR";
        public const string Service = "_osc._udp.local.";

        /// <summary>Where the question is sent. The tests point this at a fake app on loopback.</summary>
        public static IPEndPoint QueryTarget = new IPEndPoint(MdnsPacket.Group, MdnsPacket.Port);

        /// <summary>How long to wait for answers after each question.</summary>
        public static int ReplyWaitMillis = 1000;
        /// <summary>Between questions while nothing has answered yet.</summary>
        public static double SearchIntervalSeconds = 2.0;
        /// <summary>Between questions once the app is known, to catch a restart.</summary>
        public static double RefreshIntervalSeconds = 10.0;
        /// <summary>No answer for this long after having had one, and the Port setting takes over again.</summary>
        public static double LostAfterSeconds = 35.0;

        private static readonly object Gate = new object();
        private static Thread _thread;
        private static volatile bool _running;
        private static readonly Random Ids = new Random();

        // Written by the thread under Gate, read by the main thread.
        private static IPEndPoint _found;
        private static DateTime _lastAnswerUtc = DateTime.MinValue;
        private static DateTime _lastQueryUtc = DateTime.MinValue;
        private static int _queries, _answers;
        private static string _lastError = "";
        private static string _lastAnswerFrom = "";

        // Main-thread-only: what has been logged so far, so Pump logs transitions and not state.
        private static string _announcedKey = "";
        private static string _announcedError = "";

        public static int Queries { get { lock (Gate) return _queries; } }
        public static int Answers { get { lock (Gate) return _answers; } }
        public static string LastError { get { lock (Gate) return _lastError; } }
        public static bool Running => _running;

        /// <summary>The app's advertised endpoint, or null while nothing is advertised.</summary>
        public static IPEndPoint Endpoint
        {
            get { lock (Gate) return _found; }
        }

        /// <summary>Where a trigger should go right now: the advertised endpoint, or the settings if there is none.</summary>
        public static (string host, int port) Target(string fallbackHost, int fallbackPort)
        {
            var ep = Endpoint;
            return ep == null ? (fallbackHost, fallbackPort) : (ep.Address.ToString(), ep.Port);
        }

        /// <summary>A few words for the panel next to the target.</summary>
        public static string Describe()
        {
            if (Endpoint != null) return "via OSC Query";
            if (!_running) return "Port setting";
            return Answers > 0 ? "Port setting; the app stopped answering" : "Port setting; nothing found over OSC Query";
        }

        public static void Start()
        {
            if (_running) return;
            _running = true;
            _thread = new Thread(Run) { IsBackground = true, Name = "StayPutVR OSC Query" };
            _thread.Start();
        }

        /// <summary>Stop asking and forget everything learned: a stopped discovery claims no target.</summary>
        public static void Stop()
        {
            _running = false;
            try { _thread?.Join(1500); } catch { }
            _thread = null;
            lock (Gate)
            {
                _found = null;
                _queries = 0;
                _answers = 0;
                _lastError = "";
                _lastAnswerFrom = "";
                _lastAnswerUtc = DateTime.MinValue;
            }
            _announcedKey = "";
            _announcedError = "";
        }

        /// <summary>
        /// Main thread. Logs when the app appears, moves or goes quiet, and when the socket
        /// itself fails. Called every frame; it only does anything when something changed.
        /// </summary>
        public static void Pump()
        {
            IPEndPoint found;
            string error, from;
            int answers;
            lock (Gate) { found = _found; error = _lastError; from = _lastAnswerFrom; answers = _answers; }

            var key = found == null ? (answers > 0 ? "lost" : "") : $"{found.Address}:{found.Port}";
            if (key != _announcedKey)
            {
                if (found != null)
                {
                    var line = _announcedKey.Length == 0 || _announcedKey == "lost"
                        ? $"OSC Query: the StayPutVR app is at {key} (answer from {from}). The Port setting is not used while it answers."
                        : $"OSC Query: the StayPutVR app moved to {key} (answer from {from}).";
                    Core.Log.Msg(line);
                    ShockLog.Line(line);
                }
                else if (key == "lost")
                {
                    var line = $"OSC Query: no answer from the StayPutVR app for {LostAfterSeconds:0} s. Sending to the Port setting until it answers again.";
                    Core.Log.Warning(line);
                    ShockLog.Line(line);
                }
                _announcedKey = key;
            }

            if (error.Length > 0 && error != _announcedError)
            {
                Core.Log.Warning($"OSC Query: {error}. Sending to the Port setting.");
                ShockLog.Line($"OSC Query failed: {error}");
                _announcedError = error;
            }
        }

        public static string Stats()
        {
            lock (Gate)
                return $"{_queries} OSC Query question(s), {_answers} answer(s)" +
                       (_found == null ? "" : $", app at {_found}") +
                       (_lastError.Length == 0 ? "" : $"; last error: {_lastError}");
        }

        // ---- the thread ---------------------------------------------------------------------

        private static void Run()
        {
            Socket socket = null;
            var buffer = new byte[4096];
            while (_running)
            {
                try
                {
                    if (socket == null)
                    {
                        socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                        socket.Bind(new IPEndPoint(IPAddress.Any, 0));
                        socket.ReceiveTimeout = 250;
                        try { socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastTimeToLive, 255); } catch { }
                        try { socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastLoopback, true); } catch { }
                    }

                    var answer = QueryOnce(socket, buffer);
                    lock (Gate)
                    {
                        _lastError = "";
                        if (answer != null)
                        {
                            _found = answer;
                            _lastAnswerUtc = DateTime.UtcNow;
                        }
                        else if (_found != null && (DateTime.UtcNow - _lastAnswerUtc).TotalSeconds > LostAfterSeconds)
                        {
                            _found = null;
                        }
                    }
                }
                catch (Exception e)
                {
                    lock (Gate) _lastError = $"{e.GetType().Name}: {e.Message}";
                    try { socket?.Dispose(); } catch { }
                    socket = null;
                }

                var wait = Endpoint != null ? RefreshIntervalSeconds : SearchIntervalSeconds;
                if (LastError.Length > 0) wait = Math.Max(wait, 5.0);
                var until = DateTime.UtcNow.AddSeconds(wait);
                while (_running && DateTime.UtcNow < until) Thread.Sleep(100);
            }
            try { socket?.Dispose(); } catch { }
        }

        /// <summary>
        /// One question out, then everything that comes back inside the wait. The first answer
        /// naming the app on this machine wins; failing that, the first from anywhere.
        /// </summary>
        private static IPEndPoint QueryOnce(Socket socket, byte[] buffer)
        {
            var id = (ushort)Ids.Next(1, 0xFFFF);
            var query = MdnsPacket.Query(id, Service);
            var local = LocalAddresses();

            lock (Gate) { _queries++; _lastQueryUtc = DateTime.UtcNow; }

            if (IsMulticast(QueryTarget.Address) && local.Count > 0)
            {
                // The app listens per interface and not on loopback, so ask on each of them;
                // MulticastInterface picks which one the next send leaves by.
                foreach (var address in local)
                {
                    try
                    {
                        socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastInterface, address.GetAddressBytes());
                        socket.SendTo(query, QueryTarget);
                    }
                    catch (SocketException) { /* an interface with no route; the others still go out */ }
                }
            }
            else
            {
                socket.SendTo(query, QueryTarget);
            }

            IPEndPoint remote = null;
            var deadline = DateTime.UtcNow.AddMilliseconds(ReplyWaitMillis);
            EndPoint from = new IPEndPoint(IPAddress.Any, 0);
            while (DateTime.UtcNow < deadline)
            {
                int n;
                try { n = socket.ReceiveFrom(buffer, ref from); }
                catch (SocketException e) when (e.SocketErrorCode == SocketError.TimedOut || e.SocketErrorCode == SocketError.WouldBlock) { continue; }
                catch (SocketException e) when (e.SocketErrorCode == SocketError.ConnectionReset) { continue; }   // ICMP from an interface nobody listens on

                if (!MdnsPacket.TryParse(buffer, n, out var replyId, out var records) || replyId != id) continue;
                if (!MdnsPacket.TryFindService(records, Instance, Service, out var port, out _, out _)) continue;

                var sender = ((IPEndPoint)from).Address;
                lock (Gate) { _answers++; _lastAnswerFrom = $"{sender}:{((IPEndPoint)from).Port}"; }

                // Address: the app on this machine is reached over loopback whichever interface
                // it answered from; an app elsewhere on the LAN is reached where it answered from.
                var onThisMachine = IPAddress.IsLoopback(sender) || local.Contains(sender);
                var candidate = new IPEndPoint(onThisMachine ? IPAddress.Loopback : sender, port);
                if (onThisMachine) return candidate;
                if (remote == null) remote = candidate;
            }
            return remote;
        }

        private static bool IsMulticast(IPAddress a)
        {
            var b = a.GetAddressBytes();
            return b.Length == 4 && b[0] >= 224 && b[0] <= 239;
        }

        /// <summary>Every IPv4 address on an interface that is up, loopback excluded.</summary>
        private static List<IPAddress> LocalAddresses()
        {
            var list = new List<IPAddress>();
            try
            {
                foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (nic.OperationalStatus != OperationalStatus.Up) continue;
                    if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    foreach (var ua in nic.GetIPProperties().UnicastAddresses)
                        if (ua.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ua.Address))
                            list.Add(ua.Address);
                }
            }
            catch (Exception) { /* fall through to a single default-interface send */ }
            return list;
        }
    }
}
