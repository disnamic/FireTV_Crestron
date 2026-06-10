using System;
using System.Collections.Generic;
using Crestron.SimplSharp;
using Crestron.SimplSharp.CrestronSockets;

namespace FireTVADB_3Series
{
    // ADB TCP socket using Crestron.SimplSharp.CrestronSockets.TCPClient.
    //
    // Threading model
    // ───────────────
    // Incoming bytes are delivered by the Crestron socket stack via the
    // OnReceive callback (fired on the Crestron I/O thread, independent of any
    // CrestronInvoke.BeginInvoke worker).  They are queued in _buffer and
    // signalled with _dataReady.
    //
    // ReceiveMsg(timeout) blocks the caller until a complete ADB message has
    // been assembled from the buffer.  This is called directly from WorkerLoop
    // on the single BeginInvoke thread — no second "ReceiveLoop" thread needed,
    // which avoids the CrestronInvoke serialisation deadlock.
    internal class AdbSocket : IDisposable
    {
        private TCPClient        _tcp;
        private readonly string  _host;
        private readonly int     _port;

        private readonly Queue<byte> _buffer    = new Queue<byte>();
        private readonly object      _bufLock   = new object();
        // manual-reset: stays Set until Reset when buffer drains
        private readonly CEvent      _dataReady = new CEvent(false, false);

        private volatile bool _connected;

        public AdbSocket(string host, int port)
        {
            _host = host;
            _port = port;
        }

        public void Connect()
        {
            _tcp = new TCPClient(_host, _port, 65536);

            CrestronConsole.PrintLine("[ADB] Calling ConnectToServer...");
            SocketErrorCodes err = _tcp.ConnectToServer();
            CrestronConsole.PrintLine("[ADB] ConnectToServer => " + err +
                                      "  status=" + _tcp.ClientStatus);

            bool ok = err == SocketErrorCodes.SOCKET_OK
                   || _tcp.ClientStatus == SocketStatus.SOCKET_STATUS_CONNECTED;
            if (!ok)
                throw new Exception("ConnectToServer failed: err=" + err +
                                    " status=" + _tcp.ClientStatus);

            _connected = true;
            _tcp.ReceiveDataAsync(OnReceive);
            CrestronConsole.PrintLine("[ADB] Socket connected, receive armed");
        }

        // Called by Crestron I/O thread — independent of any BeginInvoke thread.
        private void OnReceive(TCPClient client, int bytesReceived)
        {
            if (bytesReceived > 0)
            {
                lock (_bufLock)
                {
                    for (int i = 0; i < bytesReceived; i++)
                        _buffer.Enqueue(client.IncomingDataBuffer[i]);
                    _dataReady.Set();
                }
            }
            if (_connected && client.ClientStatus == SocketStatus.SOCKET_STATUS_CONNECTED)
                client.ReceiveDataAsync(OnReceive);
            else
                _connected = false;
        }

        public void Send(AdbMessage msg)
        {
            byte[]           data = msg.ToBytes();
            SocketErrorCodes err  = _tcp.SendData(data, data.Length);
            if (err != SocketErrorCodes.SOCKET_OK)
                throw new Exception("ADB send error: " + err);
        }

        // Blocks the caller until a complete ADB message is assembled or timeout.
        // Called directly from WorkerLoop — no separate ReceiveLoop thread needed.
        public AdbMessage ReceiveMsg(int timeoutMs)
        {
            byte[] hdr     = ReadExact(24, timeoutMs);
            uint   cmd     = AdbMessage.ReadU32(hdr, 0);
            uint   arg0    = AdbMessage.ReadU32(hdr, 4);
            uint   arg1    = AdbMessage.ReadU32(hdr, 8);
            uint   dataLen = AdbMessage.ReadU32(hdr, 12);
            byte[] data    = dataLen > 0 ? ReadExact((int)dataLen, timeoutMs) : new byte[0];
            return new AdbMessage(cmd, arg0, arg1, data);
        }

        private byte[] ReadExact(int count, int timeoutMs)
        {
            byte[]   buf      = new byte[count];
            int      got      = 0;
            DateTime deadline = DateTime.Now.AddMilliseconds(timeoutMs);

            while (got < count)
            {
                lock (_bufLock)
                {
                    while (_buffer.Count > 0 && got < count)
                        buf[got++] = _buffer.Dequeue();
                    if (_buffer.Count == 0)
                        _dataReady.Reset();
                }

                if (got >= count) break;

                if (!_connected)
                    throw new Exception("ADB socket closed by peer");

                int remaining = (int)(deadline - DateTime.Now).TotalMilliseconds;
                if (remaining <= 0)
                    throw new Exception("ADB receive timeout (" + timeoutMs + " ms)");

                _dataReady.Wait(remaining < 200 ? remaining : 200);
            }
            return buf;
        }

        public bool Connected
        {
            get
            {
                return _connected
                    && _tcp != null
                    && _tcp.ClientStatus == SocketStatus.SOCKET_STATUS_CONNECTED;
            }
        }

        public void Dispose()
        {
            _connected = false;
            try { if (_tcp != null) { _tcp.DisconnectFromServer(); _tcp = null; } }
            catch { }
        }
    }
}
