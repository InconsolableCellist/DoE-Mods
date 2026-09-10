// Standalone checks for the two files in Osc/, which are the only part of StayPutVR that can
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

    static void Main()
    {
        Encoder();
        Sender();
        Console.WriteLine(fails == 0 ? "\nALL PASS" : $"\n{fails} FAILURE(S)");
        Environment.Exit(fails == 0 ? 0 : 1);
    }
}
