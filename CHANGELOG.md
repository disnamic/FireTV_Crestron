# FireTV ADB Control – Changelog

## [2.0] – 2026-05-26  ·  First public release

Cumulative release of all development work from v1.0 through v1.3.11.
No code changes from v1.3.11 — version bump marks first public release.

### Feature summary
- ADB over IP control of Amazon Fire TV (TCP port 5555)
- Full RSA-2048 authentication with key persistence across reboots
- Async key generation — `Connect` never blocks the SIMPL+ thread
- Auto-reconnect (10s interval) after unexpected disconnect
- Navigation, playback, volume, power, search, settings key events
- Absolute volume control with 400ms debounce
- App launch via `package/Activity` string
- `Get_Launchable_Apps` — discovers all installed Leanback apps into `App_Launch$[50]`
- Raw shell command input with chunked `Shell_fb$` output (≤250 bytes per update)
- Text input with Android-compatible escaping
- Wake-on-LAN via UDP broadcast
- NowPlaying: `NowPlaying_App$` (active media package) and `Is_Playing`
- All connection settings as symbol parameters (IP, port, key path, poll interval, WOL)
- `Console_Log_Output_Enable` to gate all console output at runtime
- `_SKIP_` separators for clean signal layout in SIMPL Windows

### Signal set
- **Digital inputs (28):** Console_Log_Output_Enable, Connect, Disconnect,
  Btn_Home/Back/Menu/Up/Down/Left/Right/Select, Btn_Play/Pause/PlayPause/Stop/
  FastForward/Rewind/SkipForward/SkipBack, Vol_Up/Down/Mute, Power_Toggle,
  Btn_Search, Btn_Settings, Poll_Now, WOL_Send, Get_Launchable_Apps
- **Digital outputs (2):** Is_Connected, Is_Playing
- **Analog input (1):** Set_Volume_Level (0–15)
- **Analog outputs (2):** Volume_Level, Volume_Max
- **String inputs (3):** Launch_App$, Send_Shell$, Input_Text$
- **String outputs (4+array):** Current_App$, NowPlaying_App$, Shell_fb$, App_Launch$[50]

---

## Development history (internal)

### [1.3.11] – Hardening
- CTimer leak in volume debounce callback fixed (dispose instead of null)
- O(n²) desync resync replaced with forward-scan + single shift
- RX buffer overflow now drains buffered messages before flushing, preserving
  large multi-packet responses (e.g. `dumpsys window windows`)
- Auth flags `_signatureSent` / `_pubKeySent` made `volatile`
- Orphaned doc-comment removed

### [1.3.10] – Code review fixes
- `ParseAppList` collects entries before firing clear signal (FIX-2)
- App entries truncated to 250 chars for SimplSharpString limit (FIX-3)
- Shell chunking moved from SIMPL+ While loop to C# driver (FIX-5)
- `_SKIP_` before `Get_Launchable_Apps` (FIX-7)
- Single log line per app query instead of one per entry (FIX-8)

### [1.3.9] – Launchable app discovery
- `Get_Launchable_Apps` digital input
- `App_Launch$[50]` string output array
- `OnAppLaunchEntry` delegate (index 0 = clear, 1..50 = entries)
- Queries `cmd package query-activities --brief`

### [1.3.8] – Shell feedback
- `Shell_fb$` string output restored
- `OnFeedbackReceived` delegate re-added for shell output

### [1.3.7] – Cleanup
- No auto-connect on program start; RSA key pre-loaded only
- `Feedback$`, `Media_Debug_Enable`, `NowPlaying_Title$`, `NowPlaying_Artist$` removed
- `DIGITAL_OUTPUT _SKIP_` before output section corrected (was DIGITAL_INPUT)

### [1.3.3–1.3.6] – Runtime bug fixes
- `ExtractSessionBlock` off-by-one: NowPlaying metadata was always empty
- `dumpsys window windows` without grep (no PATH dependency)
- Immediate poll at CNXN (no wait for first interval)
- `package=` key fix for `NowPlaying_App$`
- `UDPServer`, `AdbAuth`, `TCPClient` dispose leaks fixed
- `_streamBuffer` changed from string concatenation to `StringBuilder`
- `_connectPending` made `volatile`

### [1.3.2] – Session extraction
- Correct session block extraction for `dumpsys media_session`
- Multi-format `PlaybackState` detection (Fire OS 6, 7+, OEM variants)
- Async RSA key init (0ms CTimer), `_connectPending` flag

### [1.3.1] – Code review port
- All v1.4.1 review fixes ported to v1.3 codebase
- `ClearStreamState` resets NowPlaying dedup fields
- `AdbAuth` accepts log delegate

### [1.3] – NowPlaying
- `dumpsys media_session` polling
- `NowPlaying_App$`, `Is_Playing` outputs

### [1.2] – Cosmetic / parameters
- All connection settings moved to symbol parameters
- `Console_Log_Output_Enable` input
- `Poll_Interval_Sec` dropdown (2–20s)
- `_SKIP_` separators between signal groups

### [1.1.2] – ADB auth
- Full RSA-2048 authentication (RSACryptoServiceProvider)
- androidpubkey encoding verified against AOSP
- Key persistence via XML file
- TCPClient leak fix, multi-chunk WRTE accumulation

### [1.1.1] – Controls
- All Android KeyCodes
- Thread-safety (ConcurrentDictionary, volatile)
- Volume debounce (400ms)
- Wake-on-LAN (Crestron UDPServer)

### [1.1] – Polling
- App polling (`Current_App$`)
- Volume polling (`Volume_Level`, `Volume_Max`)
- Volume absolute set
- Text input

### [1.0] – Initial
- Basic ADB TCP connection, navigation, playback control

---
<!--
Versioning:
  Major (x.0)   – on request / breaking changes
  Minor (2.x)   – new features / new signals
  Bugfix (2.x.x)– bug fixes
-->
