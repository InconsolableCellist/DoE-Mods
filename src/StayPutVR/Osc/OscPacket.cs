using System;
using System.Collections.Generic;
using System.Text;

namespace StayPutVR.Osc
{
    /// <summary>
    /// An OSC 1.0 message, encoded by hand. StayPutVR reads three argument shapes on the
    /// trigger paths — the type tags <c>T</c>/<c>F</c> (a bare boolean with no data), <c>i</c>
    /// and <c>f</c> — and treats anything nonzero as "fire", so those three are all this
    /// encoder produces. There is no OSC library in the build and no need for one: a trigger
    /// is one datagram of about forty bytes and nothing ever comes back.
    ///
    /// Layout, from the spec: the address as a null-terminated string padded with nulls to a
    /// multiple of four bytes; the type-tag string (a leading comma, then one tag per
    /// argument) padded the same way; then each argument's data, big-endian. <c>T</c> and
    /// <c>F</c> carry no data at all — the tag *is* the value.
    /// </summary>
    public static class OscPacket
    {
        /// <summary>A boolean, as the tag-only <c>T</c> or <c>F</c> that VRChat and its prefabs send.</summary>
        public static byte[] Bool(string address, bool value)
        {
            var bytes = new List<byte>(48);
            WriteString(bytes, address);
            WriteString(bytes, value ? ",T" : ",F");
            return bytes.ToArray();
        }

        public static byte[] Int(string address, int value)
        {
            var bytes = new List<byte>(48);
            WriteString(bytes, address);
            WriteString(bytes, ",i");
            WriteBigEndian(bytes, BitConverter.GetBytes(value));
            return bytes.ToArray();
        }

        public static byte[] Float(string address, float value)
        {
            var bytes = new List<byte>(48);
            WriteString(bytes, address);
            WriteString(bytes, ",f");
            WriteBigEndian(bytes, BitConverter.GetBytes(value));
            return bytes.ToArray();
        }

        /// <summary>ASCII, one null terminator minimum, then nulls up to a four-byte boundary.</summary>
        private static void WriteString(List<byte> into, string s)
        {
            var raw = Encoding.ASCII.GetBytes(s ?? "");
            into.AddRange(raw);
            var padding = 4 - (raw.Length % 4);   // never zero: at least one null terminator
            for (var i = 0; i < padding; i++) into.Add(0);
        }

        /// <summary>BitConverter is little-endian on every platform this runs on; OSC is big-endian.</summary>
        private static void WriteBigEndian(List<byte> into, byte[] littleEndian)
        {
            for (var i = littleEndian.Length - 1; i >= 0; i--) into.Add(littleEndian[i]);
        }

        /// <summary>An address is legal if it starts with a slash and has no spaces or OSC wildcards.</summary>
        public static bool IsUsableAddress(string address)
        {
            if (string.IsNullOrWhiteSpace(address)) return false;
            if (address[0] != '/') return false;
            foreach (var c in address)
            {
                if (c <= ' ' || c > '~') return false;
                if (c == '#' || c == '*' || c == ',' || c == '?' || c == '[' || c == ']' || c == '{' || c == '}') return false;
            }
            return true;
        }
    }
}
