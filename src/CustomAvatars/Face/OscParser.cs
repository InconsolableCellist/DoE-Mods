using System;
using System.Collections.Generic;
using System.Text;

namespace CustomAvatars.Face
{
    /// <summary>
    /// Just enough OSC 1.0 to read what VRCFaceTracking sends: messages and bundles, with
    /// float, int, bool and string arguments. Everything is big-endian and padded to 4 bytes.
    ///
    /// Hand-rolled rather than taking a dependency: the format is small and completely
    /// specified, and a MelonLoader mod that drags in a NuGet package is a package every friend
    /// has to have sitting next to the DLL.
    /// </summary>
    public static class OscParser
    {
        public struct Message
        {
            public string Address;
            public float Float;
            public bool HasFloat;
            public bool Bool;
            public bool HasBool;
        }

        private static readonly byte[] BundlePrefix = Encoding.ASCII.GetBytes("#bundle\0");

        /// <summary>Parse one datagram, appending every message found to <paramref name="into"/>.</summary>
        public static void Parse(byte[] data, int length, List<Message> into)
        {
            if (data == null || length < 4) return;
            ParseElement(data, 0, length, into, 0);
        }

        private static void ParseElement(byte[] data, int offset, int end, List<Message> into, int depth)
        {
            if (depth > 4 || offset >= end) return;   // bundles nest, but not deeply

            if (IsBundle(data, offset, end))
            {
                // "#bundle" + 8-byte timetag, then [int32 length][element] repeated.
                var pos = offset + 8 + 8;
                while (pos + 4 <= end)
                {
                    var size = ReadInt(data, pos);
                    pos += 4;
                    if (size <= 0 || pos + size > end) return;
                    ParseElement(data, pos, pos + size, into, depth + 1);
                    pos += size;
                }
                return;
            }

            var message = ParseMessage(data, offset, end);
            if (message.Address != null) into.Add(message);
        }

        private static bool IsBundle(byte[] data, int offset, int end)
        {
            if (offset + BundlePrefix.Length > end) return false;
            for (var i = 0; i < BundlePrefix.Length; i++)
                if (data[offset + i] != BundlePrefix[i]) return false;
            return true;
        }

        private static Message ParseMessage(byte[] data, int offset, int end)
        {
            var message = new Message();

            var address = ReadString(data, ref offset, end);
            if (address == null || address.Length == 0 || address[0] != '/') return message;

            var tags = ReadString(data, ref offset, end);
            if (tags == null || tags.Length == 0 || tags[0] != ',') return message;

            for (var i = 1; i < tags.Length; i++)
            {
                switch (tags[i])
                {
                    case 'f':
                        if (offset + 4 > end) return message;
                        if (!message.HasFloat) { message.Float = ReadFloat(data, offset); message.HasFloat = true; }
                        offset += 4;
                        break;
                    case 'i':
                        if (offset + 4 > end) return message;
                        if (!message.HasFloat) { message.Float = ReadInt(data, offset); message.HasFloat = true; }
                        offset += 4;
                        break;
                    // T and F carry no payload at all — the type tag IS the value.
                    case 'T': if (!message.HasBool) { message.Bool = true; message.HasBool = true; } break;
                    case 'F': if (!message.HasBool) { message.Bool = false; message.HasBool = true; } break;
                    case 's': ReadString(data, ref offset, end); break;
                    case 'd': offset += 8; break;
                    case 'b':
                        if (offset + 4 > end) return message;
                        var blobSize = ReadInt(data, offset);
                        offset += 4 + Pad(blobSize);
                        break;
                    default: return message;   // unknown tag: the rest can't be located
                }
            }

            message.Address = address;
            return message;
        }

        private static string ReadString(byte[] data, ref int offset, int end)
        {
            var start = offset;
            while (offset < end && data[offset] != 0) offset++;
            if (offset >= end) return null;
            var text = Encoding.ASCII.GetString(data, start, offset - start);
            offset = start + Pad(offset - start + 1);   // include the terminator, then pad to 4
            return text;
        }

        private static int Pad(int n) => (n + 3) & ~3;

        private static int ReadInt(byte[] d, int o) => (d[o] << 24) | (d[o + 1] << 16) | (d[o + 2] << 8) | d[o + 3];

        private static float ReadFloat(byte[] d, int o)
        {
            // OSC is big-endian; x86 is not.
            var bits = ReadInt(d, o);
            return BitConverter.Int32BitsToSingle(bits);
        }

        /// <summary>Build a minimal OSC message with a single bool argument.</summary>
        public static byte[] BuildBool(string address, bool value)
        {
            var addressBytes = Encoding.ASCII.GetBytes(address);
            var tags = Encoding.ASCII.GetBytes(value ? ",T" : ",F");

            var total = Pad(addressBytes.Length + 1) + Pad(tags.Length + 1);
            var packet = new byte[total];
            Buffer.BlockCopy(addressBytes, 0, packet, 0, addressBytes.Length);
            Buffer.BlockCopy(tags, 0, packet, Pad(addressBytes.Length + 1), tags.Length);
            return packet;
        }
    }
}
