using System;

namespace FireTVADB_3Series
{
    // Minimal fixed-size 2048-bit unsigned integer.
    // Only the operations needed for computing the Android ADB RSA public key format
    // are implemented (shift-left-1, compare, subtract, from big-endian bytes).
    internal class BigUInt2048
    {
        private const int WORDS = 64; // 2048 / 32

        // Words stored least-significant first (word[0] = bits 0–31).
        private readonly uint[] _w = new uint[WORDS];

        private BigUInt2048() { }

        public static BigUInt2048 One()
        {
            var r = new BigUInt2048();
            r._w[0] = 1;
            return r;
        }

        // Build from the big-endian 256-byte RSA modulus returned by .NET.
        public static BigUInt2048 FromBigEndianBytes(byte[] src)
        {
            byte[] n = PadTo256(src);
            var r = new BigUInt2048();
            for (int i = 0; i < WORDS; i++)
            {
                // word[i] covers bytes n[255-i*4] (LSB) .. n[252-i*4] (MSB)
                int b = 255 - i * 4;
                r._w[i] = (uint)n[b]
                        | ((uint)n[b - 1] << 8)
                        | ((uint)n[b - 2] << 16)
                        | ((uint)n[b - 3] << 24);
            }
            return r;
        }

        // Double (logical left shift by 1); bits that overflow 2048 are dropped.
        public void ShiftLeft1()
        {
            uint carry = 0;
            for (int i = 0; i < WORDS; i++)
            {
                uint v = _w[i];
                _w[i]  = (v << 1) | carry;
                carry  = v >> 31;
            }
        }

        // Returns -1, 0, or +1.
        public int CompareTo(BigUInt2048 other)
        {
            for (int i = WORDS - 1; i >= 0; i--)
            {
                if (_w[i] < other._w[i]) return -1;
                if (_w[i] > other._w[i]) return  1;
            }
            return 0;
        }

        // Subtract other from this in place (assumes this >= other).
        public void SubtractInPlace(BigUInt2048 other)
        {
            ulong borrow = 0;
            for (int i = 0; i < WORDS; i++)
            {
                ulong diff = (ulong)_w[i] - other._w[i] - borrow;
                _w[i]  = (uint)diff;
                borrow = (diff >> 32) & 1;
            }
        }

        // Copy the 64 LE words into dest starting at offset.
        public void CopyToWords(uint[] dest, int offset)
        {
            for (int i = 0; i < WORDS; i++)
                dest[offset + i] = _w[i];
        }

        // Return word[0] (least significant 32-bit word).
        public uint LowWord { get { return _w[0]; } }

        private static byte[] PadTo256(byte[] src)
        {
            if (src.Length == 256) return src;
            byte[] p = new byte[256];
            Buffer.BlockCopy(src, 0, p, 256 - src.Length, src.Length);
            return p;
        }
    }
}
