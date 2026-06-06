using System;
using System.Text;
using Crestron.SimplSharp.Cryptography;

namespace FireTVADB_3Series
{
    // Converts a Crestron.SimplSharp.Cryptography.RSAParameters public key to the
    // binary format expected by the Android ADB daemon, then base64-encodes it
    // ready for an AUTH RSAPUBLICKEY message.
    //
    // Android ADB RSA public key binary layout (524 bytes total):
    //   uint32  modulus_size_words  = 64  (2048 / 32)
    //   uint32  n0inv               = -(n^(-1)) mod 2^32
    //   uint32  n[64]               = modulus in little-endian 32-bit words
    //   uint32  rr[64]              = R^2 mod n  (R = 2^2048), Montgomery param
    //   int32   exponent            = 65537
    //
    // Payload appended to RSAPUBLICKEY data:
    //   base64(524-byte struct) + " crestron@controller\0"
    internal static class AdbAndroidKey
    {
        public static byte[] BuildPayload(RSAParameters pub)
        {
            BigUInt2048 modulus = BigUInt2048.FromBigEndianBytes(pub.Modulus);

            uint   n0inv = ComputeN0Inv(modulus);
            uint[] rr    = ComputeRR(modulus);
            uint[] nw    = new uint[64];
            modulus.CopyToWords(nw, 0);

            byte[] key = new byte[524];
            int    off = 0;

            WriteU32(key, ref off, 64u);
            WriteU32(key, ref off, n0inv);
            for (int i = 0; i < 64; i++) WriteU32(key, ref off, nw[i]);
            for (int i = 0; i < 64; i++) WriteU32(key, ref off, rr[i]);
            WriteU32(key, ref off, 65537u);

            string b64  = Convert.ToBase64String(key);
            string full = b64 + " crestron@controller\0";
            return Encoding.ASCII.GetBytes(full);
        }

        // n0inv = -(n0^(-1)) mod 2^32  where n0 = least-significant 32-bit word of n
        private static uint ComputeN0Inv(BigUInt2048 modulus)
        {
            uint n0 = modulus.LowWord;
            uint x  = 1u;
            for (int i = 1; i < 32; i++)
            {
                if (((n0 * x) & (1u << i)) != 0)
                    x |= (1u << i);
            }
            return unchecked((uint)(-(int)x));
        }

        // rr = R^2 mod n  where R = 2^2048
        private static uint[] ComputeRR(BigUInt2048 modulus)
        {
            BigUInt2048 r = BigUInt2048.One();
            for (int i = 0; i < 4096; i++)
            {
                r.ShiftLeft1();
                if (r.CompareTo(modulus) >= 0)
                    r.SubtractInPlace(modulus);
            }
            uint[] result = new uint[64];
            r.CopyToWords(result, 0);
            return result;
        }

        private static void WriteU32(byte[] buf, ref int off, uint v)
        {
            buf[off]     = (byte)v;
            buf[off + 1] = (byte)(v >> 8);
            buf[off + 2] = (byte)(v >> 16);
            buf[off + 3] = (byte)(v >> 24);
            off += 4;
        }
    }
}
