// Standalone checks for the files in Osc/, which are the only part of StayPutVR that can
// be exercised without a headset. Both suites compile the mod's real source — there is no
// second copy of the encoder or the sender here — with Stubs.cs standing in for the logger,
// the session log and UnityEngine.Time.
//
//   cd src/StayPutVR/tests && dotnet run
//
// Run it from Windows, not WSL: the Windows SDK cannot read a \\wsl.localhost source path.
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using StayPutVR.Osc;

class Tests
{
    static int fails = 0;

    static void Say(bool ok, string what)
    {
        if (!ok) fails++;
        Console.WriteLine($"{(ok ? "PASS" : "FAIL")} {what}");
    }

    static void Check(string what, byte[] got, byte[] want)
    {
        var ok = got.Length == want.Length;
        if (ok) for (var i = 0; i < got.Length; i++) if (got[i] != want[i]) { ok = false; break; }
        Console.WriteLine($"{(ok ? "PASS" : "FAIL")} {what}  len={got.Length}  {BitConverter.ToString(got)}");
        if (!ok) { fails++; Console.WriteLine($"     wanted len={want.Length}  {BitConverter.ToString(want)}"); }
    }

    /// <summary>Builds an expected packet: strings go in as ASCII, ints mean that many nul bytes.</summary>
    static byte[] B(params object[] parts)
    {
        var list = new List<byte>();
        foreach (var p in parts)
        {
            if (p is string s) foreach (var c in s) list.Add((byte)c);
            else if (p is int n) for (var i = 0; i < n; i++) list.Add(0);
        }
        return list.ToArray();
    }

    static UdpClient Listen(int port) => new UdpClient(new IPEndPoint(IPAddress.Loopback, port));

    static byte[] ReceiveOne(UdpClient listener, int millis)
    {
        listener.Client.ReceiveTimeout = millis;
        var buffer = new byte[2048];
        EndPoint from = new IPEndPoint(IPAddress.Any, 0);
        try
        {
            var n = listener.Client.ReceiveFrom(buffer, ref from);
            var got = new byte[n];
            Array.Copy(buffer, got, n);
            return got;
        }
        catch (SocketException) { return null; }
    }

    static bool Same(byte[] a, byte[] b)
    {
        if (a == null || b == null || a.Length != b.Length) return false;
        for (var i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
        return true;
    }

    // ---- OscPacket: the bytes against the OSC 1.0 layout ---------------------------------
    static void Encoder()
    {
        Console.WriteLine("== OscPacket ==");

        const string Shock = "/avatar/parameters/Shock";   // 24 chars -> a whole extra word of nuls

        Check("bool true, 24-char address", OscPacket.Bool(Shock, true), B(Shock, 4, ",T", 2));
        Check("bool false", OscPacket.Bool(Shock, false), B(Shock, 4, ",F", 2));
        Check("bool, 10-char address", OscPacket.Bool("/SPVR_Bite", true), B("/SPVR_Bite", 2, ",T", 2));
        Check("address already on a word boundary still gets a terminator",
              OscPacket.Bool("/abc", true), B("/abc", 4, ",T", 2));

        // int 1 big-endian = 00 00 00 01
        Check("int 1 is big-endian", OscPacket.Int(Shock, 1),
              B(Shock, 4, ",i", 2, 3, ""));

        // 1.0f = 0x3F800000
        var wantFloat = new List<byte>(B(Shock, 4, ",f", 2));
        wantFloat.AddRange(new byte[] { 0x3F, 0x80, 0x00, 0x00 });
        Check("float 1.0 is big-endian IEEE754", OscPacket.Float(Shock, 1f), wantFloat.ToArray());

        var addresses = new (string addr, bool want)[]
        {
            (Shock, true), ("/a", true), ("/SPVR_Bite_Thigh_Left", true),
            ("", false), (null, false), ("   ", false),
            ("avatar/parameters/Shock", false),
            ("/has space", false), ("/wild*card", false), ("/q?", false),
            ("/br[a]cket", false), ("/cur{ly}", false), ("/ha#sh", false), ("/com,ma", false),
            ("/unié", false),
        };
        foreach (var (addr, want) in addresses)
        {
            var got = OscPacket.IsUsableAddress(addr);
            if (got != want) fails++;
            Console.WriteLine($"{(got == want ? "PASS" : "FAIL")} IsUsableAddress({(addr == null ? "null" : "\"" + addr + "\"")}) = {got}");
        }

        // Every OSC packet must be a whole number of 4-byte words.
        var ragged = 0;
        for (var len = 1; len <= 40; len++)
        {
            var addr = "/" + new string('x', len - 1);
            foreach (var d in new[] { OscPacket.Bool(addr, true), OscPacket.Int(addr, 1), OscPacket.Float(addr, 1f) })
                if (d.Length % 4 != 0) { ragged++; Console.WriteLine($"FAIL address length {len} produced {d.Length} bytes"); }
        }
        fails += ragged;
        if (ragged == 0) Console.WriteLine("PASS every packet, address lengths 1..40, is a multiple of 4 bytes");
    }

    // ---- OscSender: the real socket, over loopback ---------------------------------------
    static void Sender()
    {
        Console.WriteLine("\n== OscSender ==");

        const string Shock = "/avatar/parameters/Shock";

        // ---- 1. a trigger and its release arrive byte-identical -------------------------
        var portA = 19001;
        using (var listener = Listen(portA))
        {
            Say(OscSender.Ensure("127.0.0.1", portA), "Ensure opens a socket to 127.0.0.1:" + portA);
            Say(OscSender.TargetDescription == $"127.0.0.1:{portA}", $"TargetDescription is \"127.0.0.1:{portA}\", got \"{OscSender.TargetDescription}\"");

            var trigger = OscPacket.Bool(Shock, true);
            Say(OscSender.Send(trigger, "trigger"), "the trigger reports sent");
            var got = ReceiveOne(listener, 1500);
            Say(Same(got, trigger), $"the trigger arrives byte-identical ({(got == null ? "nothing arrived" : got.Length + " bytes")})");
            if (got != null) Console.WriteLine("      " + BitConverter.ToString(got));

            var release = OscPacket.Bool(Shock, false);
            Say(OscSender.Send(release, "release"), "the release reports sent");
            Say(Same(ReceiveOne(listener, 1500), release), "the release arrives byte-identical");

            // Every value type, so a config change cannot silently produce garbage.
            foreach (var (name, datagram) in new (string, byte[])[]
                     { ("int", OscPacket.Int(Shock, 1)), ("float", OscPacket.Float(Shock, 1f)) })
            {
                OscSender.Send(datagram, name);
                Say(Same(ReceiveOne(listener, 1500), datagram), $"a {name} trigger arrives byte-identical");
            }
        }

        // ---- 2. changing the port retargets, and the old port stops receiving -----------
        var portB = 19002;
        using (var listenerB = Listen(portB))
        {
            Say(OscSender.Ensure("127.0.0.1", portB), "Ensure retargets to " + portB);
            var trigger = OscPacket.Bool(Shock, true);
            OscSender.Send(trigger, "trigger");
            Say(Same(ReceiveOne(listenerB, 1500), trigger), "the trigger arrives at the new port");
        }

        // ---- 3. sending with nothing listening must not poison the next send ------------
        // This is the reason the socket is left unconnected: a connected UDP socket turns the
        // ICMP port-unreachable into a ConnectionReset on the NEXT send.
        var portC = 19003;
        OscSender.Ensure("127.0.0.1", portC);
        var before = OscSender.Failed;
        for (var i = 0; i < 3; i++) OscSender.Send(OscPacket.Bool(Shock, true), "into the void");
        Say(OscSender.Failed == before, $"three sends to a dead port report no failure (Failed {before} -> {OscSender.Failed})");

        using (var listenerC = Listen(portC))
        {
            var trigger = OscPacket.Bool(Shock, true);
            Say(OscSender.Send(trigger, "trigger"), "a send right after those still reports sent");
            Say(Same(ReceiveOne(listenerC, 1500), trigger), "and it arrives — the dead-port sends did not poison the socket");
        }

        // ---- 4. a bad target is refused, not thrown -------------------------------------
        Say(!OscSender.Ensure("127.0.0.1", 0), "port 0 is refused");
        Say(!OscSender.Ensure("127.0.0.1", 70000), "port 70000 is refused");
        Say(!OscSender.Ensure("no.such.host.invalid", 9001), "an unresolvable host is refused");
        Say(!OscSender.Send(OscPacket.Bool(Shock, true), "no socket"), "a send with no socket reports failure rather than throwing");
        Say(OscSender.LastFailureAt > 0f, "the failure timestamp is set, so the overlay can show trouble");

        Say(OscSender.Ensure("127.0.0.1", 9001), "and it recovers on the next good target");
        OscSender.Close();
    }

    // ---- MdnsPacket: the question, and the app's answer ------------------------------------
    // The answer bytes are what the StayPutVR app sends back: tests/MdnsAnswerDump.cpp calls
    // the app's own vendored mdns library with the records common/OSCQueryServer.cpp answers
    // with (StayPutVR on DESKTOP-TEST.local., port 51234, address 192.168.1.20, id 0x1234).
    // Re-run that program and re-copy if the app's answer ever changes.
    static readonly byte[] AppAnswer =
    {
        0x12, 0x34, 0x84, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00, 0x00, 0x02, 0x04, 0x5F, 0x6F, 0x73,
        0x63, 0x04, 0x5F, 0x75, 0x64, 0x70, 0x05, 0x6C, 0x6F, 0x63, 0x61, 0x6C, 0x00, 0x00, 0x0C, 0x80,
        0x01, 0xC0, 0x0C, 0x00, 0x0C, 0x00, 0x01, 0x00, 0x00, 0x00, 0x0A, 0x00, 0x0C, 0x09, 0x53, 0x74,
        0x61, 0x79, 0x50, 0x75, 0x74, 0x56, 0x52, 0xC0, 0x0C, 0xC0, 0x2D, 0x00, 0x21, 0x00, 0x01, 0x00,
        0x00, 0x00, 0x78, 0x00, 0x15, 0x00, 0x00, 0x00, 0x00, 0xC8, 0x22, 0x0C, 0x44, 0x45, 0x53, 0x4B,
        0x54, 0x4F, 0x50, 0x2D, 0x54, 0x45, 0x53, 0x54, 0xC0, 0x16, 0xC0, 0x4B, 0x00, 0x01, 0x00, 0x01,
        0x00, 0x00, 0x00, 0x78, 0x00, 0x04, 0xC0, 0xA8, 0x01, 0x14,
    };

    // The question the mod sends for id 0x1234; MdnsAnswerDump.cpp carries the same bytes and
    // pushes them through the app library's listen path.
    static readonly byte[] ModQuery =
    {
        0x12, 0x34, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x04, (byte)'_', (byte)'o', (byte)'s', (byte)'c', 0x04, (byte)'_', (byte)'u', (byte)'d', (byte)'p',
        0x05, (byte)'l', (byte)'o', (byte)'c', (byte)'a', (byte)'l', 0x00,
        0x00, 0x0C, 0x80, 0x01,
    };

    static byte[] WithId(byte[] packet, ushort id)
    {
        var c = (byte[])packet.Clone();
        c[0] = (byte)(id >> 8);
        c[1] = (byte)id;
        return c;
    }

    /// <summary>The fixture with its SRV port (0xC8 0x22 = 51234, the only such pair in it) replaced.</summary>
    static byte[] WithPort(byte[] packet, ushort port)
    {
        var c = (byte[])packet.Clone();
        var at = -1;
        for (var i = 0; i + 1 < c.Length; i++) if (c[i] == 0xC8 && c[i + 1] == 0x22) { at = i; break; }
        if (at < 0) throw new Exception("port bytes not found in the fixture");
        c[at] = (byte)(port >> 8);
        c[at + 1] = (byte)port;
        return c;
    }

    /// <summary>The fixture advertising a different instance of the same length, so every compression pointer still lands.</summary>
    static byte[] Renamed(byte[] packet, string from, string to)
    {
        if (from.Length != to.Length) throw new ArgumentException("same length only");
        var c = (byte[])packet.Clone();
        var f = System.Text.Encoding.ASCII.GetBytes(from);
        for (var i = 0; i + f.Length <= c.Length; i++)
        {
            var hit = true;
            for (var j = 0; j < f.Length && hit; j++) hit = c[i + j] == f[j];
            if (hit) { for (var j = 0; j < f.Length; j++) c[i + j] = (byte)to[j]; return c; }
        }
        throw new Exception($"'{from}' not found in the fixture");
    }

    static void Mdns()
    {
        Console.WriteLine("\n== MdnsPacket ==");

        Check("PTR question for _osc._udp.local., id 0x1234, unicast-response bit",
              MdnsPacket.Query(0x1234, "_osc._udp.local."), ModQuery);
        Say(Same(MdnsPacket.Query(0x1234, "_osc._udp.local"), ModQuery), "the name without its trailing dot encodes the same");

        Say(MdnsPacket.TryParse(AppAnswer, AppAnswer.Length, out var id, out var records), "the app's answer parses");
        Say(id == 0x1234, $"the id is echoed back (got 0x{id:X4})");
        Say(records.Count == 3, $"three records: one answer and two additionals (got {records.Count}: {string.Join("; ", records)})");

        var ptr = records.Find(r => r.Type == MdnsPacket.TypePtr);
        Say(ptr != null && ptr.Name == "_osc._udp.local." && ptr.Target == "StayPutVR._osc._udp.local.",
            $"the PTR's compressed names are followed (got {ptr})");
        var srv = records.Find(r => r.Type == MdnsPacket.TypeSrv);
        Say(srv != null && srv.Name == "StayPutVR._osc._udp.local." && srv.Target == "DESKTOP-TEST.local." && srv.Port == 51234,
            $"the SRV carries the host and port (got {srv})");
        var a = records.Find(r => r.Type == MdnsPacket.TypeA);
        Say(a != null && a.Name == "DESKTOP-TEST.local." && a.Address.ToString() == "192.168.1.20", $"the A record carries the address (got {a})");

        Say(MdnsPacket.TryFindService(records, "StayPutVR", "_osc._udp.local.", out var port, out var host, out var address)
            && port == 51234 && host == "DESKTOP-TEST.local." && address != null && address.ToString() == "192.168.1.20",
            $"TryFindService finds StayPutVR at {host}:{port} = {address}");
        Say(MdnsPacket.TryFindService(records, "stayputvr", "_OSC._udp.local", out var port2, out _, out _) && port2 == 51234,
            "matching is case-insensitive and does not need the trailing dot");
        Say(!MdnsPacket.TryFindService(records, "VRChat", "_osc._udp.local.", out _, out _, out _), "another instance name is not matched");
        Say(!MdnsPacket.TryFindService(records, "StayPutVR", "_oscjson._tcp.local.", out _, out _, out _), "another service type is not matched");

        var renamed = Renamed(AppAnswer, "StayPutVR", "VRChatXYZ");
        Say(MdnsPacket.TryParse(renamed, renamed.Length, out _, out var r2)
            && !MdnsPacket.TryFindService(r2, "StayPutVR", "_osc._udp.local.", out _, out _, out _)
            && MdnsPacket.TryFindService(r2, "VRChatXYZ", "_osc._udp.local.", out var p3, out _, out _) && p3 == 51234,
            "the same answer from another app is parsed but not taken for StayPutVR");

        Say(!MdnsPacket.TryParse(ModQuery, ModQuery.Length, out _, out _), "a query is not accepted as a response");

        // Anything can arrive on a UDP socket; a cut-off packet must be refused, never thrown on.
        var threw = 0;
        var accepted = 0;
        for (var len = 0; len < AppAnswer.Length; len++)
        {
            try
            {
                if (MdnsPacket.TryParse(AppAnswer, len, out _, out var rr)
                    && MdnsPacket.TryFindService(rr, "StayPutVR", "_osc._udp.local.", out _, out _, out _)) accepted++;
            }
            catch (Exception e) { threw++; Console.WriteLine($"      len {len} threw {e.GetType().Name}"); }
        }
        Say(threw == 0 && accepted == 0, $"every truncation of the answer (0..{AppAnswer.Length - 1} bytes) is refused without throwing ({threw} threw, {accepted} accepted)");
    }

    // ---- Discovery: the thread, against a fake app on loopback ----------------------------
    sealed class FakeApp : IDisposable
    {
        private readonly UdpClient _sock;
        private readonly Thread _thread;
        private volatile bool _running = true;
        public volatile bool Answer = true;
        public volatile byte[] Reply;
        public readonly int Port;
        public int Seen;

        public FakeApp(byte[] reply)
        {
            Reply = reply;
            _sock = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
            _sock.Client.ReceiveTimeout = 200;
            Port = ((IPEndPoint)_sock.Client.LocalEndPoint).Port;
            _thread = new Thread(Run) { IsBackground = true };
            _thread.Start();
        }

        private void Run()
        {
            while (_running)
            {
                var from = new IPEndPoint(IPAddress.Any, 0);
                byte[] q;
                try { q = _sock.Receive(ref from); } catch (SocketException) { continue; }
                Seen++;
                if (!Answer || q.Length < 2) continue;
                // Legacy unicast: straight back to the asker's own port, with its id.
                var reply = WithId(Reply, (ushort)((q[0] << 8) | q[1]));
                _sock.Send(reply, reply.Length, from);
            }
        }

        public void Dispose()
        {
            _running = false;
            _thread.Join(1000);
            _sock.Dispose();
        }
    }

    static bool WaitFor(Func<bool> condition, int millis)
    {
        var until = DateTime.UtcNow.AddMilliseconds(millis);
        while (DateTime.UtcNow < until)
        {
            Discovery.Pump();
            if (condition()) return true;
            Thread.Sleep(50);
        }
        Discovery.Pump();
        return condition();
    }

    static int LogCount(string fragment) => StayPutVR.Logger.Lines.FindAll(l => l.Contains(fragment)).Count;

    static void Finder()
    {
        Console.WriteLine("\n== Discovery ==");
        Discovery.SearchIntervalSeconds = 0.3;
        Discovery.RefreshIntervalSeconds = 0.3;
        Discovery.LostAfterSeconds = 1.0;
        Discovery.ReplyWaitMillis = 300;

        using (var app = new FakeApp(AppAnswer))
        {
            Discovery.QueryTarget = new IPEndPoint(IPAddress.Loopback, app.Port);
            var (h0, p0) = Discovery.Target("127.0.0.1", 9001);
            Say(h0 == "127.0.0.1" && p0 == 9001, "before anything answers, Target is the fallback");
            Say(Discovery.Describe().StartsWith("Port setting"), $"and the panel says so (\"{Discovery.Describe()}\")");

            Discovery.Start();
            Say(WaitFor(() => Discovery.Endpoint != null, 3000), $"the app is found within 3 s ({Discovery.Stats()})");
            var ep = Discovery.Endpoint;
            Say(ep != null && IPAddress.IsLoopback(ep.Address) && ep.Port == 51234, $"an app on this machine is reached as loopback:51234 (got {ep})");
            var (h1, p1) = Discovery.Target("127.0.0.1", 9001);
            Say(h1 == "127.0.0.1" && p1 == 51234, $"Target is now the advertised port ({h1}:{p1})");
            Say(Discovery.Describe() == "via OSC Query", $"the panel says via OSC Query (\"{Discovery.Describe()}\")");
            Say(LogCount("the StayPutVR app is at 127.0.0.1:51234") == 1, "the arrival was logged, once");
            Thread.Sleep(800);
            Say(LogCount("the StayPutVR app is at 127.0.0.1:51234") == 1, "and repeated answers do not log again");
            Say(app.Seen >= 2, $"the question is repeated while the app is known ({app.Seen} seen)");

            // The app goes quiet: after LostAfterSeconds the Port setting takes over.
            app.Answer = false;
            Say(WaitFor(() => Discovery.Endpoint == null, 4000), "after the app stops answering, the endpoint is dropped");
            var (h2, p2) = Discovery.Target("127.0.0.1", 9001);
            Say(h2 == "127.0.0.1" && p2 == 9001, "Target is the fallback again");
            Say(LogCount("no answer from the StayPutVR app") == 1, "the loss was logged, once");
            Say(Discovery.Describe().Contains("stopped answering"), $"the panel says the app stopped answering (\"{Discovery.Describe()}\")");

            // It comes back on another port, as a restarted app does.
            app.Reply = WithPort(AppAnswer, 51235);
            app.Answer = true;
            Say(WaitFor(() => Discovery.Endpoint != null && Discovery.Endpoint.Port == 51235, 3000), $"the app back on another port is picked up ({Discovery.Endpoint})");
            Say(LogCount("the StayPutVR app is at 127.0.0.1:51235") == 1, "the return was logged");

            // And moves without going quiet first.
            app.Reply = WithPort(AppAnswer, 51236);
            Say(WaitFor(() => Discovery.Endpoint != null && Discovery.Endpoint.Port == 51236, 3000), $"a port change is picked up ({Discovery.Endpoint})");
            Say(LogCount("moved to 127.0.0.1:51236") == 1, "the move was logged");

            Discovery.Stop();
            Say(!Discovery.Running && Discovery.Endpoint == null, "Stop clears the endpoint");
        }

        // Another OSC app answering the same question is not taken for StayPutVR.
        using (var other = new FakeApp(Renamed(AppAnswer, "StayPutVR", "VRChatXYZ")))
        {
            Discovery.QueryTarget = new IPEndPoint(IPAddress.Loopback, other.Port);
            Discovery.Start();
            var found = WaitFor(() => Discovery.Endpoint != null, 1500);
            Say(!found && other.Seen > 0, $"an answer for another instance name is ignored ({other.Seen} question(s) seen, {Discovery.Answers} taken)");
            Discovery.Stop();
        }

        // Nothing listening at all: no answer, no error, still the fallback.
        Discovery.QueryTarget = new IPEndPoint(IPAddress.Loopback, 19004);
        Discovery.Start();
        Thread.Sleep(1000);
        Discovery.Pump();
        Say(Discovery.Endpoint == null && Discovery.LastError.Length == 0, $"a dead target is silence, not an error ({Discovery.Stats()})");
        Discovery.Stop();
    }

    static void Main()
    {
        Encoder();
        Sender();
        Mdns();
        Finder();
        Console.WriteLine(fails == 0 ? "\nALL PASS" : $"\n{fails} FAILURE(S)");
        Environment.Exit(fails == 0 ? 0 : 1);
    }
}
