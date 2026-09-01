using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace DoEFriendsMod.Face
{
    /// <summary>
    /// Receives VRCFaceTracking's OSC output by standing in for VRChat.
    ///
    /// The awkward part of this turned out not to be necessary. VRCFT's send target is a plain
    /// configured address and port (`LocalSettings.json` → `OSCAddress`, `OSCOutPort`), and
    /// OSCQuery discovery never overrides it — so there is no mDNS responder and no HTTP
    /// OSCQuery server to write. We just listen.
    ///
    /// The one catch is that VRCFT only sends `/avatar/parameters/...` for parameters it
    /// believes the receiver has declared, which normally means answering an OSCQuery request
    /// or matching a VRChat avatar config on disk. It also accepts
    /// `/vrcft/settings/forceRelevant`, which switches every parameter on. One UDP packet
    /// replaces the entire discovery mechanism.
    ///
    /// Separately, `/tracking/eye/*` is sent unconditionally, so gaze and eyelids arrive even
    /// with no negotiation at all.
    /// </summary>
    public class VrcftBridge : IDisposable
    {
        public FaceState State { get; } = new FaceState();

        private UdpClient _socket;
        private Thread _thread;
        private volatile bool _running;
        private readonly List<OscParser.Message> _scratch = new List<OscParser.Message>();

        public bool Running => _running;
        public int ListenPort { get; private set; }

        public void Start()
        {
            if (_running) return;

            ListenPort = Math.Clamp(ModConfig.FaceOscListenPort.Value, 1, 65535);
            try
            {
                _socket = new UdpClient(new IPEndPoint(IPAddress.Loopback, ListenPort));
                _socket.Client.ReceiveTimeout = 500;
            }
            catch (Exception e)
            {
                Core.Log.Error($"Face OSC: could not listen on 127.0.0.1:{ListenPort} — {e.Message}. " +
                               "Another program may already hold that port (VRChat itself uses 9000).");
                return;
            }

            _running = true;
            _thread = new Thread(ReceiveLoop) { IsBackground = true, Name = "DFM-FaceOSC" };
            _thread.Start();

            Core.Log.Msg($"Face OSC listening on 127.0.0.1:{ListenPort}.");
            Core.Log.Msg($"  Point VRCFaceTracking at it: OSCAddress=127.0.0.1, OSCOutPort={ListenPort} " +
                         "in %AppData%\\VRCFaceTracking\\VRCFaceTracking\\ApplicationData\\LocalSettings.json");

            if (ModConfig.FaceForceRelevant.Value) SendForceRelevant();
        }

        /// <summary>
        /// Ask VRCFT to send every parameter rather than only ones a declared avatar uses.
        /// Sent from loopback because VRCFT refuses to bind anything else.
        /// </summary>
        public void SendForceRelevant()
        {
            var port = Math.Clamp(ModConfig.FaceOscSendPort.Value, 1, 65535);
            try
            {
                using var sender = new UdpClient();
                var packet = OscParser.BuildBool("/vrcft/settings/forceRelevant", true);
                sender.Send(packet, packet.Length, new IPEndPoint(IPAddress.Loopback, port));
                Core.Log.Msg($"Face OSC: asked VRCFaceTracking (127.0.0.1:{port}) to send all parameters.");
            }
            catch (Exception e)
            {
                Core.Log.Warning($"Face OSC: could not reach VRCFaceTracking on 127.0.0.1:{port} — {e.Message}. " +
                                 "Only the always-on /tracking/eye values will arrive.");
            }
        }

        private void ReceiveLoop()
        {
            var endpoint = new IPEndPoint(IPAddress.Any, 0);
            while (_running)
            {
                try
                {
                    var data = _socket.Receive(ref endpoint);
                    _scratch.Clear();
                    OscParser.Parse(data, data.Length, _scratch);

                    for (var i = 0; i < _scratch.Count; i++)
                    {
                        var message = _scratch[i];
                        if (message.Address == null) continue;
                        if (message.HasFloat) State.Set(message.Address, message.Float);
                        else if (message.HasBool) State.Set(message.Address, message.Bool ? 1f : 0f);
                    }
                }
                catch (SocketException) { /* receive timeout; loop and check _running */ }
                catch (ObjectDisposedException) { return; }
                catch (Exception e)
                {
                    // Never let one malformed datagram end the thread.
                    try { Core.Log.Warning($"Face OSC receive error: {e.GetType().Name}: {e.Message}"); } catch { }
                }
            }
        }

        public void Dispose()
        {
            _running = false;
            try { _socket?.Close(); } catch { }
            try { _thread?.Join(1000); } catch { }
            _socket = null;
            _thread = null;
        }
    }
}
