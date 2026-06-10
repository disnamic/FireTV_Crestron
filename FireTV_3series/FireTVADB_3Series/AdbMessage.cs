using System;

namespace FireTVADB_3Series
{
    // ADB command tags as observed on Fire TV hardware.
    // Fire TV adbd sends the command field as 4 ASCII bytes in natural string
    // order ('A','U','T','H') rather than as a little-endian uint32. ReadU32
    // therefore gives different values for AUTH and OPEN compared to the AOSP
    // constants (0x41555448 / 0x4f50454e). CNXN, OKAY, CLSE and WRTE are
    // self-consistent between the two interpretations so they are unchanged.
    internal static class AdbCmd
    {
        public const uint CNXN = 0x4e584e43; // 'C','N','X','N' → ReadU32 (unchanged)
        public const uint AUTH = 0x48545541; // 'A','U','T','H' → ReadU32
        public const uint OPEN = 0x4e45504f; // 'O','P','E','N' → ReadU32
        public const uint OKAY = 0x59414b4f; // 'O','K','A','Y' → ReadU32 (unchanged)
        public const uint CLSE = 0x45534c43; // 'C','L','S','E' → ReadU32 (unchanged)
        public const uint WRTE = 0x45545257; // 'W','R','T','E' → ReadU32 (unchanged)
    }

    internal static class AdbAuthType
    {
        public const uint TOKEN        = 1;
        public const uint SIGNATURE    = 2;
        public const uint RSAPUBLICKEY = 3;
    }

    internal class AdbMessage
    {
        public const uint VERSION  = 0x01000000;
        public const uint MAXDATA  = 256 * 1024;

        public uint   Command;
        public uint   Arg0;
        public uint   Arg1;
        public byte[] Data;

        public AdbMessage() { Data = new byte[0]; }

        public AdbMessage(uint cmd, uint arg0, uint arg1, byte[] data)
        {
            Command = cmd;
            Arg0    = arg0;
            Arg1    = arg1;
            Data    = data ?? new byte[0];
        }

        public byte[] ToBytes()
        {
            int  dataLen  = Data != null ? Data.Length : 0;
            uint checksum = Checksum(Data);
            uint magic    = Command ^ 0xFFFFFFFFu;

            byte[] pkt = new byte[24 + dataLen];
            WriteU32(pkt, 0,  Command);
            WriteU32(pkt, 4,  Arg0);
            WriteU32(pkt, 8,  Arg1);
            WriteU32(pkt, 12, (uint)dataLen);
            WriteU32(pkt, 16, checksum);
            WriteU32(pkt, 20, magic);
            if (dataLen > 0)
                Buffer.BlockCopy(Data, 0, pkt, 24, dataLen);
            return pkt;
        }

        public static uint Checksum(byte[] data)
        {
            if (data == null) return 0;
            uint s = 0;
            for (int i = 0; i < data.Length; i++) s += data[i];
            return s;
        }

        public static void WriteU32(byte[] buf, int off, uint v)
        {
            buf[off]     = (byte)v;
            buf[off + 1] = (byte)(v >> 8);
            buf[off + 2] = (byte)(v >> 16);
            buf[off + 3] = (byte)(v >> 24);
        }

        public static uint ReadU32(byte[] buf, int off)
        {
            return (uint)buf[off]
                 | ((uint)buf[off + 1] << 8)
                 | ((uint)buf[off + 2] << 16)
                 | ((uint)buf[off + 3] << 24);
        }
    }
}
