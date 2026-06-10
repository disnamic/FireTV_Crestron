using System;
using System.Text;
using Crestron.SimplSharp;

namespace FireTVADB_3Series
{
    /*
     * FireTVDriver — Crestron 3-Series SimplSharp public API
     *
     * Key fix: SetConsoleLog() and SetPollingInterval() are called from SIMPL+
     * Main() BEFORE Initialize() creates the AdbClient.  Both methods previously
     * checked "if (_client != null)" and silently discarded the values, so the
     * new client always started with defaults (logging off, 5 s poll).
     * Settings are now stored at the driver level and applied every time a new
     * client is created inside Initialize().
     */

    // ── Delegate types ───────────────────────────────────────────────────────
    public delegate void ConnectionChangedDelegate(ushort status);
    public delegate void StringFeedbackDelegate(SimplSharpString text);
    public delegate void VolumeLevelDelegate(ushort level, ushort maxLevel);
    public delegate void IsPlayingDelegate(ushort state);
    public delegate void AppLaunchEntryDelegate(ushort idx, SimplSharpString value);

    // ─────────────────────────────────────────────────────────────────────────
    public class FireTVDriver
    {
        public ConnectionChangedDelegate OnConnectionChanged  { get; set; }
        public StringFeedbackDelegate    OnFeedbackReceived   { get; set; }
        public StringFeedbackDelegate    OnCurrentAppChanged  { get; set; }
        public VolumeLevelDelegate       OnVolumeLevelChanged { get; set; }
        public StringFeedbackDelegate    OnNowPlayingApp      { get; set; }
        public IsPlayingDelegate         OnIsPlaying          { get; set; }
        public AppLaunchEntryDelegate    OnAppLaunchEntry     { get; set; }

        // ── private state ────────────────────────────────────────────────────
        private AdbClient _client;
        private AdbAuth   _auth;
        private string    _ip;
        private int       _port    = 5555;
        private string    _keyPath;
        private bool      _initialized;
        private readonly object _initLock = new object();

        // Driver-level settings — persisted so Initialize() can apply them to
        // whatever AdbClient it creates, regardless of call order.
        private bool _consoleLogEnabled = false;
        private int  _driverPollMs      = 5000;

        // Volume debounce
        private ushort _pendingVolume;
        private bool   _volPending;
        private CTimer _volTimer;
        private readonly object _volLock = new object();

        // ── Public API ───────────────────────────────────────────────────────

        public void Initialize(string ip, ushort port, string keyPath)
        {
            lock (_initLock)
            {
                _ip      = ip;
                _port    = port > 0 ? (int)port : 5555;
                _keyPath = string.IsNullOrEmpty(keyPath) ? null : keyPath;

                CrestronConsole.PrintLine("[FireTV] Initialize: ip=" + _ip + " port=" + _port);

                if (_auth   != null) { _auth.Dispose();                    _auth   = null; }
                if (_client != null) { _client.Stop(); _client.Dispose();  _client = null; }

                _auth = new AdbAuth(_keyPath);
                _auth.Initialize();

                _client = new AdbClient();
                WireCallbacks(_client);
                _client.Configure(_ip, _port, _auth);

                // Apply driver-level settings that may have been set before
                // Initialize() was called (e.g. from SIMPL+ Main()).
                _client.SetConsoleLog(_consoleLogEnabled);
                _client.SetPollInterval(_driverPollMs);

                _initialized = true;
                CrestronConsole.PrintLine("[FireTV] Initialize complete");
            }
        }

        public void Connect()
        {
            if (!_initialized)
            {
                CrestronConsole.PrintLine("[FireTV] Connect called before Initialize — ignored");
                return;
            }
            CrestronConsole.PrintLine("[FireTV] Connect called");
            _client.Start();
        }

        public void Disconnect()
        {
            CrestronConsole.PrintLine("[FireTV] Disconnect called");
            if (_client != null) _client.Disconnect();
        }

        public void SetConsoleLog(ushort enable)
        {
            _consoleLogEnabled = enable != 0;
            if (_client != null) _client.SetConsoleLog(_consoleLogEnabled);
        }

        public void SetPollingInterval(ushort seconds)
        {
            _driverPollMs = seconds < 2 ? 5000 : (int)seconds * 1000;
            if (_client != null) _client.SetPollInterval(_driverPollMs);
        }

        // ── Navigation / Media keys ──────────────────────────────────────────

        public void SendKeyEvent(int keyCode)
        {
            Shell("input keyevent " + keyCode);
        }

        public void LaunchApp(string packageActivity)
        {
            if (!string.IsNullOrEmpty(packageActivity))
                Shell("am start -n " + packageActivity + " 2>/dev/null");
        }

        // ── Shell ────────────────────────────────────────────────────────────

        public void ShellRaw(string command)
        {
            if (!string.IsNullOrEmpty(command) && _client != null)
                _client.EnqueueShellWithOutput(command, ShellOutputCallback);
        }

        private void ShellOutputCallback(string raw) { FireShellChunked(raw); }

        // ── Text input ───────────────────────────────────────────────────────

        public void SendText(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            string esc = text.Replace("\\", "\\\\")
                             .Replace("'",  "\\'")
                             .Replace(" ",  "%s")
                             .Replace("\"", "\\\"");
            Shell("input text '" + esc + "'");
        }

        // ── Volume ───────────────────────────────────────────────────────────

        public void SetVolumeDebounced(ushort level)
        {
            lock (_volLock)
            {
                _pendingVolume = level;
                _volPending    = true;
                if (_volTimer == null)
                    _volTimer = new CTimer(VolTimerCallback, null, 300);
                else
                    _volTimer.Reset(300);
            }
        }

        // ── Polling / discovery ───────────────────────────────────────────────

        public void PollNow()
        {
            if (_client != null) _client.PollNow();
        }

        public void QueryLaunchableApps()
        {
            if (_client != null) _client.QueryLaunchableApps();
        }

        // ── WOL ──────────────────────────────────────────────────────────────

        public void WakeOnLan(string mac, string broadcastIp)
        {
            try
            {
                string bcast = string.IsNullOrEmpty(broadcastIp) ? "255.255.255.255" : broadcastIp;
                WolSender.Send(mac, bcast);
                CrestronConsole.PrintLine("[FireTV] WOL sent to " + mac);
            }
            catch (Exception ex)
            {
                CrestronConsole.PrintLine("[FireTV] WOL error: " + ex.Message);
            }
        }

        // ── Private helpers ───────────────────────────────────────────────────

        private void Shell(string cmd)
        {
            if (_client != null) _client.EnqueueShell(cmd);
        }

        private void VolTimerCallback(object userSpecific)
        {
            ushort level;
            lock (_volLock)
            {
                if (!_volPending) return;
                level       = _pendingVolume;
                _volPending = false;
            }
            Shell("media volume --stream 3 --set " + level + " 2>/dev/null");
        }

        private void FireShellChunked(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return;
            StringFeedbackDelegate cb = OnFeedbackReceived;
            if (cb == null) return;
            byte[] bytes = Encoding.UTF8.GetBytes(raw);
            int sent = 0;
            while (sent < bytes.Length)
            {
                int    take  = Math.Min(250, bytes.Length - sent);
                string chunk = Encoding.UTF8.GetString(bytes, sent, take);
                cb(new SimplSharpString(chunk));
                sent += take;
            }
        }

        // ── Callback wiring ───────────────────────────────────────────────────

        private void WireCallbacks(AdbClient c)
        {
            c.OnConnectionChanged  = ConnectionChangedCb;
            c.OnCurrentAppChanged  = CurrentAppChangedCb;
            c.OnVolumeLevelChanged = VolumeLevelChangedCb;
            c.OnNowPlayingApp      = NowPlayingAppCb;
            c.OnIsPlaying          = IsPlayingCb;
            c.OnAppLaunchEntry     = AppLaunchEntryCb;
        }

        private void ConnectionChangedCb(bool s)
        {
            CrestronConsole.PrintLine("[FireTV] ConnectionChanged: " + (s ? "CONNECTED" : "DISCONNECTED"));
            ConnectionChangedDelegate cb = OnConnectionChanged;
            if (cb != null) cb(s ? (ushort)1 : (ushort)0);
        }

        private void CurrentAppChangedCb(string v)
        {
            StringFeedbackDelegate cb = OnCurrentAppChanged;
            if (cb != null) cb(new SimplSharpString(v ?? string.Empty));
        }

        private void VolumeLevelChangedCb(int cur, int max)
        {
            VolumeLevelDelegate cb = OnVolumeLevelChanged;
            if (cb != null) cb((ushort)cur, (ushort)max);
        }

        private void NowPlayingAppCb(string v)
        {
            StringFeedbackDelegate cb = OnNowPlayingApp;
            if (cb != null) cb(new SimplSharpString(v ?? string.Empty));
        }

        private void IsPlayingCb(bool v)
        {
            IsPlayingDelegate cb = OnIsPlaying;
            if (cb != null) cb(v ? (ushort)1 : (ushort)0);
        }

        private void AppLaunchEntryCb(int idx, string val)
        {
            AppLaunchEntryDelegate cb = OnAppLaunchEntry;
            if (cb != null) cb((ushort)idx, new SimplSharpString(val ?? string.Empty));
        }
    }
}
