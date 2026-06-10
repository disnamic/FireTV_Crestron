using System;
using System.Collections.Generic;
using System.Text;
using Crestron.SimplSharp; // CEvent, CrestronInvoke, CrestronConsole

namespace FireTVADB_3Series
{
    // ADB protocol client for Crestron 3-Series.
    //
    // Threading model
    // ───────────────
    // Single CrestronInvoke.BeginInvoke thread runs WorkerLoop.
    //
    // IMPORTANT: CrestronInvoke.BeginInvoke on 3-Series serialises work items.
    // A second BeginInvoke queued while WorkerLoop is blocking would never run,
    // causing a deadlock.  Therefore there is NO separate ReceiveLoop thread.
    // WaitForMessage() calls _sock.ReceiveMsg() directly — the socket buffer is
    // filled by the Crestron I/O callback (OnReceive in AdbSocket), which fires
    // independently of any BeginInvoke thread.
    internal class AdbClient : IDisposable
    {
        // ── callbacks ────────────────────────────────────────────────────────
        public Action<bool>        OnConnectionChanged;
#pragma warning disable 649
        public Action<string>      OnShellOutput;
#pragma warning restore 649
        public Action<string>      OnCurrentAppChanged;
        public Action<int, int>    OnVolumeLevelChanged;
        public Action<string>      OnNowPlayingApp;
        public Action<bool>        OnIsPlaying;
        public Action<int, string> OnAppLaunchEntry;

        // ── change-detection caches (sentinel values) ────────────────────────
        private string _pubCurrentApp    = "\x01";
        private string _pubNowPlayingApp = "\x01";
        private int    _pubIsPlaying     = -1;
        private int    _pubVolumeCur     = -1;
        private int    _pubVolumeMax     = -1;

        // ── config ───────────────────────────────────────────────────────────
        private string  _host;
        private int     _port;
        private AdbAuth _auth;
        private int     _pollIntervalMs = 5000;
        private bool    _consoleLog;

        // ── threading ────────────────────────────────────────────────────────
        private volatile bool  _workerRunning;
        private readonly object _lock = new object();

        private readonly Queue<string> _shellQueue  = new Queue<string>();
        private readonly Queue<Action> _actionQueue = new Queue<Action>();
        private readonly CEvent        _queueReady  = new CEvent(true, false);

        private AdbSocket _sock;
        private bool      _disposed;

        // ── public ───────────────────────────────────────────────────────────

        public void Configure(string host, int port, AdbAuth auth)
        {
            _host = host;
            _port = port;
            _auth = auth;
        }

        public void SetPollInterval(int ms) { _pollIntervalMs = ms; }
        public void SetConsoleLog(bool on)  { _consoleLog = on;     }

        public void Start()
        {
            lock (_lock)
            {
                if (_workerRunning) return;
                _workerRunning = true;
                CrestronInvoke.BeginInvoke(o => WorkerLoop());
            }
        }

        public void Stop()
        {
            _workerRunning = false;
            _queueReady.Set();
        }

        public void EnqueueShell(string cmd)
        {
            lock (_shellQueue) _shellQueue.Enqueue(cmd);
            _queueReady.Set();
        }

        public void EnqueueShellWithOutput(string cmd, Action<string> onDone)
        {
            EnqueueAction(delegate
            {
                string result = RunShell(cmd);
                if (onDone != null) onDone(result);
            });
        }

        public void EnqueueAction(Action a)
        {
            lock (_actionQueue) _actionQueue.Enqueue(a);
            _queueReady.Set();
        }

        public void Disconnect()
        {
            CloseSocket();
            FireConnectionChanged(false);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Stop();
            CloseSocket();
        }

        // ── worker loop ──────────────────────────────────────────────────────

        private void WorkerLoop()
        {
            DateTime nextPoll = DateTime.Now;

            while (_workerRunning)
            {
                if (_sock == null || !_sock.Connected)
                {
                    if (!TryConnect())
                    {
                        _queueReady.Wait(10000);
                        continue;
                    }
                }

                bool didWork = DrainQueues();

                if (DateTime.Now >= nextPoll)
                {
                    nextPoll = DateTime.Now.AddMilliseconds(_pollIntervalMs);
                    DoPoll();
                }

                if (!didWork)
                    _queueReady.Wait(500);
            }

            CloseSocket();
        }

        private bool DrainQueues()
        {
            bool did = false;
            while (true)
            {
                Action a     = null;
                string shell = null;
                lock (_actionQueue) if (_actionQueue.Count > 0) a = _actionQueue.Dequeue();
                if (a == null)
                    lock (_shellQueue) if (_shellQueue.Count > 0) shell = _shellQueue.Dequeue();

                if (a != null)          { try { a(); }           catch { } did = true; }
                else if (shell != null) { try { RunShell(shell); } catch { } did = true; }
                else break;
            }
            return did;
        }

        // ── ADB connect / auth ───────────────────────────────────────────────

        private bool TryConnect()
        {
            try
            {
                LogAlways("[ADB] Connecting to " + _host + ":" + _port);
                CloseSocket();

                _sock = new AdbSocket(_host, _port);
                _sock.Connect();

                // Send CNXN — no second BeginInvoke thread; WaitForMessage reads
                // directly from the socket buffer filled by the Crestron I/O callback.
                byte[] cnxnData = Encoding.ASCII.GetBytes("host::features=shell_v2,cmd\0");
                _sock.Send(new AdbMessage(AdbCmd.CNXN, AdbMessage.VERSION, AdbMessage.MAXDATA, cnxnData));
                LogAlways("[ADB] CNXN sent");

                // Auth handshake — up to 3 rounds
                for (int round = 0; round < 3; round++)
                {
                    AdbMessage msg = WaitForMessage(10000);
                    if (msg == null) throw new Exception("Timeout waiting for AUTH/CNXN");

                    LogAlways("[ADB] Rx cmd=0x" + msg.Command.ToString("X8") +
                               " arg0=" + msg.Arg0 + " dataLen=" + (msg.Data != null ? msg.Data.Length : 0));

                    if (msg.Command == AdbCmd.CNXN)
                    {
                        LogAlways("[ADB] Connected (device sent CNXN)");
                        FireConnectionChanged(true);
                        return true;
                    }

                    if (msg.Command == AdbCmd.AUTH && msg.Arg0 == AdbAuthType.TOKEN)
                    {
                        LogAlways("[ADB] AUTH token received — signing");
                        byte[] sig = _auth.SignToken(msg.Data);
                        _sock.Send(new AdbMessage(AdbCmd.AUTH, AdbAuthType.SIGNATURE, 0, sig));

                        AdbMessage reply = WaitForMessage(5000);
                        if (reply != null && reply.Command == AdbCmd.CNXN)
                        {
                            LogAlways("[ADB] Connected (signature accepted)");
                            FireConnectionChanged(true);
                            return true;
                        }

                        LogAlways("[ADB] Signature rejected — sending RSA public key");
                        byte[] pub = _auth.GetPublicKeyPayload();
                        _sock.Send(new AdbMessage(AdbCmd.AUTH, AdbAuthType.RSAPUBLICKEY, 0, pub));
                        LogAlways("[ADB] Waiting for user to accept on TV (up to 30 s)...");
                        reply = WaitForMessage(30000);
                        if (reply != null && reply.Command == AdbCmd.CNXN)
                        {
                            LogAlways("[ADB] Connected (public key accepted)");
                            FireConnectionChanged(true);
                            return true;
                        }
                        throw new Exception("Auth rejected or timed out");
                    }
                }
                throw new Exception("Unexpected auth sequence");
            }
            catch (Exception ex)
            {
                LogAlways("[ADB] Connect failed: " + ex.Message);
                CloseSocket();
                return false;
            }
        }

        // ── shell command ────────────────────────────────────────────────────

        private string RunShell(string cmd)
        {
            if (_sock == null || !_sock.Connected) return string.Empty;

            uint localId  = NextLocalId();
            uint remoteId = 0;
            _sock.Send(new AdbMessage(AdbCmd.OPEN, localId, 0,
                       Encoding.UTF8.GetBytes("shell:" + cmd + "\0")));

            var      sb       = new StringBuilder();
            DateTime deadline = DateTime.Now.AddSeconds(10);
            bool     open     = false;

            while (DateTime.Now < deadline)
            {
                int remaining = (int)(deadline - DateTime.Now).TotalMilliseconds;
                if (remaining <= 0) break;
                AdbMessage msg = WaitForMessage(remaining);
                if (msg == null) break;

                if (msg.Command == AdbCmd.OKAY && msg.Arg1 == localId)
                {
                    remoteId = msg.Arg0;
                    open     = true;
                    deadline = DateTime.Now.AddSeconds(8);
                    continue;
                }
                if (!open) continue;

                if (msg.Command == AdbCmd.WRTE && msg.Arg1 == localId)
                {
                    sb.Append(Encoding.UTF8.GetString(msg.Data, 0, msg.Data.Length));
                    _sock.Send(new AdbMessage(AdbCmd.OKAY, localId, remoteId, null));
                    deadline = DateTime.Now.AddSeconds(5);
                    continue;
                }
                if (msg.Command == AdbCmd.CLSE && msg.Arg1 == localId)
                {
                    _sock.Send(new AdbMessage(AdbCmd.CLSE, localId, remoteId, null));
                    break;
                }
            }
            return sb.ToString();
        }

        // ── polling ──────────────────────────────────────────────────────────

        private void DoPoll()
        {
            if (_sock == null || !_sock.Connected) return;
            PollCurrentApp();
            PollVolume();
            PollNowPlaying();
        }

        private void PollCurrentApp()
        {
            string raw = RunShell(
                "dumpsys window windows 2>/dev/null | grep -m 1 mCurrentFocus");
            string app = ParseBracketField(raw);
            if (!string.IsNullOrEmpty(app))
            {
                string pkg = app;
                int sl = pkg.IndexOf('/');
                if (sl > 0) pkg = pkg.Substring(0, sl);
                int sp = pkg.LastIndexOf(' ');
                if (sp >= 0) pkg = pkg.Substring(sp + 1);
                FireIfChanged(OnCurrentAppChanged, ref _pubCurrentApp, pkg);
            }
        }

        private void PollVolume()
        {
            string raw = RunShell("media volume --stream 3 --get-volume-steps 2>/dev/null");
            int cur, max;
            ParseVolumeOutput(raw, out cur, out max);
            if (max > 0) FireIfChanged(OnVolumeLevelChanged, ref _pubVolumeCur, ref _pubVolumeMax, cur, max);
        }

        private void PollNowPlaying()
        {
            string raw = RunShell(
                "dumpsys media_session 2>/dev/null | grep -E 'state=PlaybackState|package=' | head -4");
            ParseNowPlaying(raw);
        }

        // ── parsers ──────────────────────────────────────────────────────────

        private static string ParseBracketField(string line)
        {
            int s = line.IndexOf('{'), e = line.IndexOf('}');
            if (s < 0 || e < 0 || e <= s) return null;
            string last = null;
            foreach (string t in SplitNonEmpty(line.Substring(s + 1, e - s - 1).Trim(), ' '))
                last = t;
            return last;
        }

        private static void ParseVolumeOutput(string raw, out int cur, out int max)
        {
            cur = 0; max = 0;
            if (string.IsNullOrEmpty(raw)) return;
            foreach (string line in SplitLines(raw))
            {
                if (line.IndexOf("Current:") >= 0)
                {
                    string[] t = SplitNonEmpty(line.Trim(), ' ');
                    if (t.Length >= 4) { cur = ParseInt(t[1]); max = ParseInt(t[3]); }
                    return;
                }
                int idx = line.IndexOf("index=");
                if (idx >= 0)
                {
                    string s2 = line.Substring(idx + 6);
                    int cm = s2.IndexOf(',');
                    cur = ParseInt(cm > 0 ? s2.Substring(0, cm) : s2);
                    max = 15;
                }
            }
        }

        private void ParseNowPlaying(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return;
            string app = null; bool playing = false;
            foreach (string line in SplitLines(raw))
            {
                int pi = line.IndexOf("package=");
                if (pi >= 0)
                {
                    int s = pi + 8, e = line.IndexOf(',', s);
                    app = (e > s ? line.Substring(s, e - s) : line.Substring(s)).Trim();
                }
                if (line.IndexOf("PlaybackState") >= 0 && line.IndexOf("state=3") >= 0)
                    playing = true;
            }
            if (!string.IsNullOrEmpty(app))
                FireIfChanged(OnNowPlayingApp, ref _pubNowPlayingApp, app);
            FireIfChanged(OnIsPlaying, ref _pubIsPlaying, playing);
        }

        private void ParseAppList(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return;
            Fire(OnAppLaunchEntry, 0, string.Empty);
            int idx = 1;
            foreach (string line in SplitLines(raw))
            {
                if (line.StartsWith("package:"))
                {
                    string pkg = line.Substring(8).Trim();
                    if (!string.IsNullOrEmpty(pkg) && idx <= 50)
                        Fire(OnAppLaunchEntry, idx++, pkg);
                }
            }
        }

        // ── message I/O — reads directly from socket, no ReceiveLoop thread ──

        // WaitForMessage reads directly from the AdbSocket buffer.
        // OnReceive (Crestron I/O callback) fills that buffer independently of
        // any BeginInvoke thread, so there is no CrestronInvoke serialisation risk.
        private AdbMessage WaitForMessage(int timeoutMs)
        {
            if (timeoutMs <= 0 || _sock == null) return null;
            try { return _sock.ReceiveMsg(timeoutMs); }
            catch { return null; }
        }

        // ── helpers ──────────────────────────────────────────────────────────

        private uint _localIdSeed = 1;
        private uint NextLocalId()
        {
            lock (_lock) { uint id = _localIdSeed++; if (_localIdSeed == 0) _localIdSeed = 1; return id; }
        }

        private void CloseSocket()
        {
            try { if (_sock != null) { _sock.Dispose(); _sock = null; } } catch { }
        }

        private void FireConnectionChanged(bool v)
        {
            try { if (OnConnectionChanged != null) OnConnectionChanged(v); } catch { }
        }

        // ── change-detection fire helpers (BluOS_3Series pattern) ────────────

        private static void FireIfChanged(Action<string> del, ref string cache, string value)
        {
            if (cache == value) return;
            cache = value;
            if (del == null) return;
            try { del(value); } catch { }
        }

        private static void FireIfChanged(Action<bool> del, ref int cache, bool value)
        {
            int v = value ? 1 : 0;
            if (cache == v) return;
            cache = v;
            if (del == null) return;
            try { del(value); } catch { }
        }

        private static void FireIfChanged(Action<int, int> del, ref int cacheA, ref int cacheB, int a, int b)
        {
            if (cacheA == a && cacheB == b) return;
            cacheA = a; cacheB = b;
            if (del == null) return;
            try { del(a, b); } catch { }
        }

        private void Fire(Action<int, string> cb, int a, string b)
        {
            try { if (cb != null) cb(a, b); } catch { }
        }

        private static void LogAlways(string msg) { CrestronConsole.PrintLine(msg); }

        private void Log(string msg)
        {
            if (_consoleLog) CrestronConsole.PrintLine(msg);
        }

        private static string[] SplitNonEmpty(string s, char sep)
        {
            var list = new List<string>();
            foreach (string p in s.Split(new char[] { sep }))
                if (p.Length > 0) list.Add(p);
            return list.ToArray();
        }

        private static string[] SplitLines(string s)
        {
            return SplitNonEmpty(s.Replace("\r\n", "\n").Replace("\r", "\n"), '\n');
        }

        private static int ParseInt(string s)
        {
            try { return Int32.Parse(s.Trim()); } catch { return 0; }
        }

        public void QueryLaunchableApps()
        {
            EnqueueAction(delegate { QueryLaunchableAppsWorker(); });
        }

        private void QueryLaunchableAppsWorker()
        {
            // Query ALL apps (system + third-party) that register a LEANBACK_LAUNCHER
            // intent — this is what the Fire TV home screen uses to build its app list.
            // Returns every launchable app including pre-installed ones like BritBox.
            //
            // Output per match (2 lines):
            //   priority=0 preferredOrder=0 match=0x108000 ...
            //   com.netflix.ninja/.MainActivity
            string raw = RunShell(
                "cmd package query-activities --brief " +
                "-a android.intent.action.MAIN " +
                "-c android.intent.category.LEANBACK_LAUNCHER 2>/dev/null");

            if (string.IsNullOrEmpty(raw)) return;

            Fire(OnAppLaunchEntry, 0, string.Empty); // clear list
            int idx = 1;

            foreach (string line in SplitLines(raw))
            {
                // Activity lines contain a '/' — skip priority/match lines
                if (line.IndexOf('/') < 0) continue;
                string entry = line.Trim();
                if (string.IsNullOrEmpty(entry)) continue;
                if (idx > 50) break;
                Fire(OnAppLaunchEntry, idx++, entry);
            }

            // If LEANBACK_LAUNCHER returned nothing, fall back to standard LAUNCHER
            if (idx <= 1)
            {
                raw = RunShell(
                    "cmd package query-activities --brief " +
                    "-a android.intent.action.MAIN " +
                    "-c android.intent.category.LAUNCHER 2>/dev/null");

                if (string.IsNullOrEmpty(raw)) return;

                foreach (string line in SplitLines(raw))
                {
                    if (line.IndexOf('/') < 0) continue;
                    string entry = line.Trim();
                    if (string.IsNullOrEmpty(entry)) continue;
                    if (idx > 50) break;
                    Fire(OnAppLaunchEntry, idx++, entry);
                }
            }
        }

        public void PollNow()
        {
            EnqueueAction(delegate { DoPoll(); });
        }
    }
}
