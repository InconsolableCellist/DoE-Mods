using UnityEngine;

namespace CustomAvatars.Net
{
    /// <summary>
    /// Shared pose quantization for the byte streams. Hand and face sync predate this file and
    /// quantize inline (a byte each); poses are the first payload big enough to deserve real
    /// helpers rather than a third copy of the pattern.
    ///
    /// Positions: 16-bit fixed point, millimetre resolution, ±32.7 m — plenty for anything
    /// expressed relative to a player's own play-space root. Rotations: smallest-three, the
    /// standard trick — drop a quaternion's largest component (recoverable from the other
    /// three since |q| = 1), store which it was in 2 bits and the rest as 10-bit fixed point.
    /// Four bytes for ~0.1° of error, which is under what hand tremor produces.
    /// </summary>
    public static class Packing
    {
        private const float SqrtHalf = 0.70710678f;   // remaining components live in ±1/√2

        // ---- positions (6 bytes) ----------------------------------------------------------

        public static void WritePosMm(byte[] buffer, ref int offset, Vector3 v)
        {
            WriteShort(buffer, ref offset, ToMm(v.x));
            WriteShort(buffer, ref offset, ToMm(v.y));
            WriteShort(buffer, ref offset, ToMm(v.z));
        }

        public static Vector3 ReadPosMm(byte[] buffer, ref int offset)
        {
            return new Vector3(
                ReadShort(buffer, ref offset) * 0.001f,
                ReadShort(buffer, ref offset) * 0.001f,
                ReadShort(buffer, ref offset) * 0.001f);
        }

        private static short ToMm(float metres) =>
            (short)Mathf.Clamp(Mathf.RoundToInt(metres * 1000f), short.MinValue, short.MaxValue);

        private static void WriteShort(byte[] buffer, ref int offset, short value)
        {
            buffer[offset++] = (byte)(value & 0xFF);
            buffer[offset++] = (byte)((value >> 8) & 0xFF);
        }

        private static short ReadShort(byte[] buffer, ref int offset)
        {
            var value = (short)(buffer[offset] | (buffer[offset + 1] << 8));
            offset += 2;
            return value;
        }

        // ---- rotations (4 bytes) ----------------------------------------------------------

        public static void WriteQuat(byte[] buffer, ref int offset, Quaternion q)
        {
            // q and -q are the same rotation; flip so the dropped component is positive and
            // needs no sign bit.
            var largest = 0;
            var components = new float[] { q.x, q.y, q.z, q.w };
            for (var i = 1; i < 4; i++)
                if (Mathf.Abs(components[i]) > Mathf.Abs(components[largest])) largest = i;
            if (components[largest] < 0f)
                for (var i = 0; i < 4; i++) components[i] = -components[i];

            uint packed = (uint)largest << 30;
            var shift = 20;
            for (var i = 0; i < 4; i++)
            {
                if (i == largest) continue;
                var scaled = (uint)Mathf.Clamp(
                    Mathf.RoundToInt((components[i] / SqrtHalf * 0.5f + 0.5f) * 1023f), 0, 1023);
                packed |= scaled << shift;
                shift -= 10;
            }

            buffer[offset++] = (byte)(packed & 0xFF);
            buffer[offset++] = (byte)((packed >> 8) & 0xFF);
            buffer[offset++] = (byte)((packed >> 16) & 0xFF);
            buffer[offset++] = (byte)((packed >> 24) & 0xFF);
        }

        public static Quaternion ReadQuat(byte[] buffer, ref int offset)
        {
            var packed = (uint)(buffer[offset] | (buffer[offset + 1] << 8) |
                                (buffer[offset + 2] << 16) | (buffer[offset + 3] << 24));
            offset += 4;

            var largest = (int)(packed >> 30);
            var components = new float[4];
            var sumSq = 0f;
            var shift = 20;
            for (var i = 0; i < 4; i++)
            {
                if (i == largest) continue;
                var scaled = (packed >> shift) & 0x3FF;
                components[i] = (scaled / 1023f - 0.5f) * 2f * SqrtHalf;
                sumSq += components[i] * components[i];
                shift -= 10;
            }
            components[largest] = Mathf.Sqrt(Mathf.Max(0f, 1f - sumSq));

            var q = new Quaternion(components[0], components[1], components[2], components[3]);
            // Quantization nudges the length off 1; the solver would happily amplify that.
            return Quaternion.Normalize(q);
        }
    }
}
