# FireTV ADB Control v2.0 — Release Notes

**Release date:** 2026-05-26
**First public release**

---

## What is this?

A Crestron 4-Series SIMPL# Library + SIMPL+ module that provides full control of
Amazon Fire TV devices over ADB (Android Debug Bridge) via TCP/IP — no USB cable,
no additional hardware.

---

## What's included

| File | Description |
|---|---|
| `FireTVADB.clz` | SIMPL# Library — add to SIMPL Windows |
| `FireTV_Control_v2.0.usp` | SIMPL+ module — add to your program |
| `FireTV_LaunchApp_Reference.txt` | Device-specific app launch strings (tab-separated) |
| `README.md` | Full signal reference and setup guide |
| `CHANGELOG.md` | Complete development history |

---

## Highlights

**Full ADB authentication** — RSA-2048 key pair generated on first use, stored on
the controller's flash. Subsequent connections are fully automatic — no TV confirmation
required.

**Complete transport control** — all standard Android media key events: play, pause,
play/pause, stop, fast forward, rewind, skip forward/back.

**App discovery and launch** — pulse `Get_Launchable_Apps` to populate 50 string
outputs with the launch strings of all installed apps. Pass any string directly to
`Launch_App$` to start that app. No hardcoded package names required.

**Shell access** — send any ADB shell command via `Send_Shell$`, receive output in
`Shell_fb$`. Useful for diagnostics, retrieving device info, or anything not covered
by dedicated signals.

**NowPlaying** — `NowPlaying_App$` shows which app is currently playing media.
`Is_Playing` reflects the active playback state. Title/artist are not implemented
as most streaming apps (Netflix, Prime Video) do not expose this data via the
Android media session API.

**Non-blocking design** — RSA key generation runs asynchronously. `Initialize()`
returns immediately; `Connect` proceeds automatically once the key is ready. No
SIMPL+ thread blocking.

**Robust RX handling** — large responses (`dumpsys window windows`, app list) are
accumulated across multiple ADB WRTE packets and delivered complete. Buffer overflow
handling preserves in-progress large responses instead of discarding them.

---

## Requirements

- Crestron 4-Series processor (CP4, MC4, or compatible)
- SIMPL Windows with SIMPL# support
- Fire TV with ADB Debugging + Network Debugging enabled
  (Settings → My Fire TV → Developer Options)
- Fire TV on the same network as the Crestron processor (or routed)

---

## Known limitations

- **Netflix** and some other streaming apps suppress media session metadata —
  `NowPlaying_App$` shows the package name but title/artist are unavailable.
- **Volume** controls `STREAM_MUSIC`. May not apply to HDMI ARC / optical passthrough
  depending on the active audio output mode.
- **App launch strings are device-specific.** Package names and activity names vary
  between Fire OS versions and generations. Always use `Get_Launchable_Apps` to get
  the correct strings for the target device.
- **Wake-on-LAN** is included for completeness but is rarely effective — Fire TV
  typically keeps its network stack alive in suspend mode and is reachable via ADB
  without WOL.

---

## Quick start

1. Enable ADB Debugging and Network Debugging on the Fire TV
2. In SIMPL Windows: add `FireTVADB.clz`, add `FireTV_Control_v2.0.usp`
3. Set the `FireTV_IP$` parameter to the Fire TV's IP address
4. Set `Key_Store_Path$` to `\user\FireTV_ADBKey.xml`
5. Compile and load the program
6. Pulse `Connect` — confirm the dialog on the TV screen (once only)
7. All subsequent connections are automatic

---

## Feedback and issues

This module was developed and tested on a 4th-generation Amazon Fire TV Stick 4K Max
running Fire OS 8. Behavior on other Fire TV generations or Fire OS versions may vary,
particularly for `dumpsys media_session` output format and available shell commands.
