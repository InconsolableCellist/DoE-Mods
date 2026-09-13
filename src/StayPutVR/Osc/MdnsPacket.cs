using System;
using System.Collections.Generic;
using System.Net;
using System.Text;

namespace StayPutVR.Osc
{
    /// <summary>
    /// mDNS on the wire, and only as much of it as finding the StayPutVR app needs: one PTR
    /// question going out, and the PTR, SRV and A records that come back. The app advertises
    /// itself as <c>StayPutVR._osc._udp.local.</c> and the SRV record on that name carries the
    /// receive port it actually bound, which is the one number this whole file exists to fetch.
    ///
    /// The format is DNS (RFC 1035) with the mDNS conventions of RFC 6762: labels are length
    /// prefixed, a name may end in a two-byte pointer back into the packet instead of more
    /// labels, and the top bit of the class field means "answer me by unicast" on the way out
    /// and "flush your cache" on the way back, so it is masked off on both.
    /// </summary>
    public static class MdnsPacket
    {
        public const int Port = 5353;
        public static readonly IPAddress Group = IPAddress.Parse("224.0.0.251");

        public const ushort TypeA = 1;
        public const ushort TypePtr = 12;
        public const ushort TypeTxt = 16;
        public const ushort TypeSrv = 33;

        private const ushort ClassIn = 1;
        private const ushort UnicastResponseBit = 0x8000;

        public sealed class Record
        {
            public string Name;
            public ushort Type;
            public uint Ttl;
            /// <summary>PTR: the instance name. SRV: the host name. Empty otherwise.</summary>
            public string Target = "";
            /// <summary>SRV only.</summary>
            public int Port;
            /// <summary>A only.</summary>
            public IPAddress Address;

            public override string ToString()
            {
                switch (Type)
                {
                    case TypePtr: return $"PTR {Name} -> {Target}";
                    case TypeSrv: return $"SRV {Name} -> {Target}:{Port}";
                    case TypeA: return $"A {Name} = {Address}";
                    default: return $"type {Type} {Name}";
                }
            }
        }

        /// <summary>
        /// One PTR question for <paramref name="name"/> (for example <c>_osc._udp.local.</c>),
        /// with the unicast-response bit set. The id is echoed back in the answer, so a reply
        /// can be told from a stray one.
        /// </summary>
        public static byte[] Query(ushort id, string name)
        {
            var bytes = new List<byte>(64);
            WriteU16(bytes, id);
            WriteU16(bytes, 0);        // flags: a standard query
            WriteU16(bytes, 1);        // one question
            WriteU16(bytes, 0);
            WriteU16(bytes, 0);
            WriteU16(bytes, 0);
            WriteName(bytes, name);
            WriteU16(bytes, TypePtr);
            WriteU16(bytes, (ushort)(ClassIn | UnicastResponseBit));
            return bytes.ToArray();
        }

        /// <summary>
        /// Every record in a response: answers, authority and additional sections alike, since
        /// the SRV and A records this needs arrive as additionals. Questions are skipped.
        /// Returns false for anything that is not a well-formed DNS response.
        /// </summary>
        public static bool TryParse(byte[] data, int length, out ushort id, out List<Record> records)
        {
            id = 0;
            records = new List<Record>();
            try
            {
                if (data == null || length < 12) return false;
                var offset = 0;
                id = ReadU16(data, ref offset);
                var flags = ReadU16(data, ref offset);
                if ((flags & 0x8000) == 0) return false;   // a query, not a response
                var questions = ReadU16(data, ref offset);
                var answers = ReadU16(data, ref offset);
                var authority = ReadU16(data, ref offset);
                var additional = ReadU16(data, ref offset);

                for (var i = 0; i < questions; i++)
                {
                    ReadName(data, length, ref offset);
                    offset += 4;
                }

                var total = answers + authority + additional;
                for (var i = 0; i < total; i++)
                {
                    if (offset >= length) return false;
                    var record = new Record { Name = ReadName(data, length, ref offset) };
                    record.Type = ReadU16(data, ref offset);
                    ReadU16(data, ref offset);   // class, with the cache-flush bit; not needed
                    record.Ttl = ReadU32(data, ref offset);
                    var rdLength = ReadU16(data, ref offset);
                    var rdStart = offset;
                    if (rdStart + rdLength > length) return false;

                    switch (record.Type)
                    {
                        case TypePtr:
                            record.Target = ReadName(data, length, ref offset);
                            break;
                        case TypeSrv:
                            ReadU16(data, ref offset);   // priority
                            ReadU16(data, ref offset);   // weight
                            record.Port = ReadU16(data, ref offset);
                            record.Target = ReadName(data, length, ref offset);
                            break;
                        case TypeA:
                            if (rdLength == 4) record.Address = new IPAddress(new[] { data[rdStart], data[rdStart + 1], data[rdStart + 2], data[rdStart + 3] });
                            break;
                    }
                    offset = rdStart + rdLength;
                    records.Add(record);
                }
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// The SRV record for <c>{instance}.{service}</c>, and the A record for its host if one
        /// came along. The PTR is not required: an announcement carries only the SRV and A, and
        /// the SRV alone has the port.
        /// </summary>
        public static bool TryFindService(IList<Record> records, string instance, string service,
                                          out int port, out string host, out IPAddress address)
        {
            port = 0;
            host = "";
            address = null;
            if (records == null) return false;
            var wanted = instance + "." + service;
            foreach (var r in records)
            {
                if (r.Type != TypeSrv || !SameName(r.Name, wanted) || r.Port <= 0) continue;
                port = r.Port;
                host = r.Target;
                foreach (var a in records)
                    if (a.Type == TypeA && a.Address != null && SameName(a.Name, host)) { address = a.Address; break; }
                return true;
            }
            return false;
        }

        /// <summary>DNS names are case-insensitive, and a trailing dot is optional.</summary>
        public static bool SameName(string a, string b)
        {
            if (a == null || b == null) return false;
            return string.Equals(a.TrimEnd('.'), b.TrimEnd('.'), StringComparison.OrdinalIgnoreCase);
        }

        // ---- encoding ---------------------------------------------------------------------

        private static void WriteU16(List<byte> into, ushort v)
        {
            into.Add((byte)(v >> 8));
            into.Add((byte)v);
        }

        /// <summary>Length-prefixed labels, then a zero. No compression on the way out.</summary>
        private static void WriteName(List<byte> into, string name)
        {
            foreach (var label in (name ?? "").Split('.'))
            {
                if (label.Length == 0) continue;
                var raw = Encoding.ASCII.GetBytes(label);
                if (raw.Length > 63) throw new ArgumentException($"label '{label}' is longer than 63 bytes");
                into.Add((byte)raw.Length);
                into.AddRange(raw);
            }
            into.Add(0);
        }

        // ---- decoding ---------------------------------------------------------------------

        private static ushort ReadU16(byte[] d, ref int o)
        {
            var v = (ushort)((d[o] << 8) | d[o + 1]);
            o += 2;
            return v;
        }

        private static uint ReadU32(byte[] d, ref int o)
        {
            var v = ((uint)d[o] << 24) | ((uint)d[o + 1] << 16) | ((uint)d[o + 2] << 8) | d[o + 3];
            o += 4;
            return v;
        }

        /// <summary>
        /// A name, following compression pointers. <paramref name="offset"/> ends up just past
        /// the name as it sits in the packet — after the first pointer, if there is one — never
        /// wherever the pointer led. Returned with a trailing dot, the way the app spells its
        /// names, so <c>_osc._udp.local.</c> compares equal without anyone remembering to add it.
        /// </summary>
        private static string ReadName(byte[] d, int length, ref int offset)
        {
            var sb = new StringBuilder(64);
            var pos = offset;
            var jumped = false;
            var hops = 0;
            while (true)
            {
                if (pos >= length) throw new IndexOutOfRangeException("name runs off the packet");
                int len = d[pos];
                if (len == 0)
                {
                    pos++;
                    break;
                }
                if ((len & 0xC0) == 0xC0)
                {
                    if (pos + 1 >= length) throw new IndexOutOfRangeException("pointer runs off the packet");
                    var pointer = ((len & 0x3F) << 8) | d[pos + 1];
                    if (!jumped) offset = pos + 2;
                    jumped = true;
                    if (++hops > 32) throw new InvalidOperationException("compression pointer loop");
                    pos = pointer;
                    continue;
                }
                if ((len & 0xC0) != 0) throw new InvalidOperationException($"unknown label type 0x{len:X2}");
                if (pos + 1 + len > length) throw new IndexOutOfRangeException("label runs off the packet");
                sb.Append(Encoding.ASCII.GetString(d, pos + 1, len));
                sb.Append('.');
                pos += 1 + len;
            }
            if (!jumped) offset = pos;
            return sb.ToString();
        }
    }
}
