using System;
using Crestron.SimplSharp.CrestronSockets;

namespace FireTVADB_3Series
{
    // WOL magic-packet sender using Crestron.SimplSharp.CrestronSockets.UDPServer.
    // (System.Net.Sockets.Socket and System.Net.IPEndPoint are not allowed in
    //  the Crestron sandbox.)
    internal static class WolSender
    {
        // mac: "AA:BB:CC:DD:EE:FF" or "AA-BB-CC-DD-EE-FF"
        public static void Send(string mac, string broadcastIp)
        {
            byte[] macBytes = ParseMac(mac);

            // Standard 102-byte magic packet: 6×0xFF then 16× MAC address
            byte[] pkt = new byte[102];
            for (int i = 0; i < 6; i++) pkt[i] = 0xFF;
            for (int rep = 0; rep < 16; rep++)
                Buffer.BlockCopy(macBytes, 0, pkt, 6 + rep * 6, 6);

            // Bind to any local port (0 = OS chooses) and send one UDP datagram
            // to port 9 of the broadcast address.
            // "0.0.0.0" = bind to any local interface; port 0 = OS-assigned ephemeral port
            UDPServer udp = new UDPServer("0.0.0.0", 0, 1024);
            try
            {
                SocketErrorCodes err = udp.EnableUDPServer();
                if (err == SocketErrorCodes.SOCKET_OK)
                    udp.SendData(pkt, pkt.Length, broadcastIp, 9);
            }
            finally
            {
                udp.DisableUDPServer();
            }
        }

        private static byte[] ParseMac(string mac)
        {
            string clean = mac.Replace(":", "").Replace("-", "").Trim();
            if (clean.Length != 12)
                throw new ArgumentException("Invalid MAC address: " + mac);
            byte[] b = new byte[6];
            for (int i = 0; i < 6; i++)
                b[i] = Convert.ToByte(clean.Substring(i * 2, 2), 16);
            return b;
        }
    }
}
