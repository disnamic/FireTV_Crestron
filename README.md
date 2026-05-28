# FireTV ADB Control – v2.0

Crestron 4-Series SIMPL# module for controlling Amazon Fire TV via ADB over IP (TCP port 5555).
Full RSA-2048 authentication, real-time feedback, launchable app discovery.

> **Platform:** 4-Series only (CP4, MC4). .NET Framework 4.7.2.

---

## First-time Setup

### Fire TV prerequisites
Settings → My Fire TV → Developer Options:
- ADB Debugging: **ON**
- Network Debugging: **ON** (port 5555)

### RSA pairing
On the first `Connect` pulse, a confirmation dialog appears on the TV screen:
**"Allow USB debugging?"** → check **"Always allow from this computer"** → **OK**.

With `Key_Store_Path$` set, this dialog only appears once. The RSA key is saved to the
controller's flash and reloaded automatically on every subsequent connection.

| `Key_Store_Path$` | Behaviour |
|---|---|
| `\user\FireTV_ADBKey.xml` | Key persists across reboots — pairing needed **once** |
| *(empty)* | In-memory key — TV must confirm after every controller reboot |

**Recommendation:** always set a path.

### Connection flow
The module does **not** connect automatically on program start. The TCP connection
is only opened when the `Connect` input is pulsed. The RSA key is pre-loaded in the
background at startup so the first `Connect` responds immediately.

---

## Symbol Parameters

| Parameter | Default | Description |
|---|---|---|
| `FireTV_IP$` | — | IP address of the Fire TV |
| `FireTV_Port` | `5555` | ADB TCP port |
| `Key_Store_Path$` | `\user\FireTV_ADBKey.xml` | RSA key file path on the controller |
| `Poll_Interval_Sec` | `10s` | Polling interval for app / volume / NowPlaying (dropdown 2–20s) |
| `WOL_Mac$` | — | MAC address for Wake-on-LAN |
| `WOL_Broadcast_IP$` | `255.255.255.255` | Broadcast address for WOL |

> **Note on Wake-on-LAN:** Fire TV typically remains reachable via ADB even in
> suspend mode, so WOL is rarely needed. It is included as a fallback in case the
> device enters a deep sleep state where the network stack is fully shut down.
> To retrieve the MAC address, send the following to `Send_Shell$`:
> ```
> ip addr show wlan0
> ```
> The MAC address appears in `Shell_fb$` on the line starting with `link/ether`.

---

## Signals

### Digital Inputs

| Signal | Description |
|---|---|
| `Console_Log_Output_Enable` | 1 = send log messages to the Crestron console |
| `Connect` | Open ADB connection to the Fire TV |
| `Disconnect` | Close connection, disable auto-reconnect |
| `Btn_Home` | KEYCODE_HOME (3) |
| `Btn_Back` | KEYCODE_BACK (4) |
| `Btn_Menu` | KEYCODE_MENU (82) |
| `Btn_Up` | KEYCODE_DPAD_UP (19) |
| `Btn_Down` | KEYCODE_DPAD_DOWN (20) |
| `Btn_Left` | KEYCODE_DPAD_LEFT (21) |
| `Btn_Right` | KEYCODE_DPAD_RIGHT (22) |
| `Btn_Select` | KEYCODE_DPAD_CENTER (23) |
| `Btn_Play` | KEYCODE_MEDIA_PLAY (126) |
| `Btn_Pause` | KEYCODE_MEDIA_PAUSE (127) |
| `Btn_PlayPause` | KEYCODE_MEDIA_PLAY_PAUSE (85) |
| `Btn_Stop` | KEYCODE_MEDIA_STOP (86) |
| `Btn_FastForward` | KEYCODE_MEDIA_FAST_FORWARD (90) |
| `Btn_Rewind` | KEYCODE_MEDIA_REWIND (89) |
| `Btn_SkipForward` | KEYCODE_MEDIA_NEXT (87) |
| `Btn_SkipBack` | KEYCODE_MEDIA_PREVIOUS (88) |
| `Vol_Up` | KEYCODE_VOLUME_UP (24) |
| `Vol_Down` | KEYCODE_VOLUME_DOWN (25) |
| `Vol_Mute` | KEYCODE_VOLUME_MUTE (164) |
| `Power_Toggle` | KEYCODE_POWER (26) |
| `Btn_Search` | KEYCODE_SEARCH (84) |
| `Btn_Settings` | KEYCODE_SETTINGS (176) |
| `Poll_Now` | Trigger an immediate poll cycle |
| `WOL_Send` | Send Wake-on-LAN magic packet |
| `Get_Launchable_Apps` | Query installed Leanback apps → populates `App_Launch$[]` |

### Digital Outputs

| Signal | Description |
|---|---|
| `Is_Connected` | 1 = ADB connection active |
| `Is_Playing` | 1 = media session PlaybackState = Playing |

### Analog Inputs

| Signal | Range | Description |
|---|---|---|
| `Set_Volume_Level` | 0–15 | Set absolute volume (400ms debounce) |

### Analog Outputs

| Signal | Description |
|---|---|
| `Volume_Level` | Current volume level |
| `Volume_Max` | Maximum volume level (typically 15) |

### String Inputs

| Signal | Description |
|---|---|
| `Launch_App$` | Launch an app: `package/Activity` format (see below) |
| `Send_Shell$` | Send a raw ADB shell command — output appears in `Shell_fb$` |
| `Input_Text$` | Type text on the Fire TV (spaces sent as `%s`) |

### String Outputs

| Signal | Description |
|---|---|
| `Current_App$` | Currently focused app (`package/Activity`) |
| `NowPlaying_App$` | Package name of the app currently playing media |
| `Shell_fb$` | Shell command output, delivered in ≤250-byte chunks |
| `App_Launch$[1..50]` | Launchable app list, populated by `Get_Launchable_Apps` |

---

## App Launch

`Launch_App$` expects a `package/Activity` string — the same format used by
`am start -n` on Android.

### Discovering installed apps
Pulse `Get_Launchable_Apps`. All installed apps registered as Leanback Launcher
activities are queried and their launch strings written to `App_Launch$[1]` through
`App_Launch$[N]`. Pass any of these directly to `Launch_App$` to start that app.

### Device-specific examples
App package names vary by Fire OS version and device. Always use
`Get_Launchable_Apps` to verify the correct string for your device.

| App | Typical Launch_App$ value |
|---|---|
| Prime Video | `com.amazon.firebat/com.amazon.pyrocore.IgnitionActivity` |
| Netflix | `com.netflix.ninja/.MainActivity` |
| YouTube | `com.amazon.firetv.youtube/dev.cobalt.app.MainActivity` |
| Disney+ | `com.disney.disneyplus/com.bamtechmedia.dominguez.main.MainActivity` |
| Kodi | `org.xbmc.kodi/.Splash` |

---

## NowPlaying

The module polls `dumpsys media_session` at the configured interval.

| Output | Content |
|---|---|
| `NowPlaying_App$` | Package name of the active media app (e.g. `com.netflix.ninja`) |
| `Is_Playing` | 1 when PlaybackState = 3 (active playback), 0 otherwise |

> **Note:** Netflix and some other apps deliberately suppress media session metadata.
> `NowPlaying_App$` is reliable; title and artist are not exposed by most streaming
> apps and are therefore not implemented.

---

## Shell Access

Send any ADB shell command to `Send_Shell$`. The output is returned via `Shell_fb$`
in chunks of up to 250 bytes. For long outputs (e.g. `dumpsys`) multiple sequential
updates will arrive.

Example commands:

| Send_Shell$ | Purpose |
|---|---|
| `ip addr show wlan0` | Get IP and MAC address |
| `dumpsys battery` | Battery / power state |
| `getprop ro.build.version.release` | Fire OS version |
| `settings get global airplane_mode_on` | Airplane mode state |

---

## Expected Console Log

### First connect (new key)
```
[FireTV] Initialize 192.168.1.x:5555 (key loading async...)
[FireTV] Generating RSA-2048 key (may take a few seconds)...
[FireTV] RSA-2048 key ready.
[FireTV] RSA key saved: \user\FireTV_ADBKey.xml
[FireTV] Connecting 192.168.1.x:5555...
[FireTV] AUTH → Signature sent (256 bytes).
[FireTV] AUTH → Public key sent. PLEASE CONFIRM ON TV SCREEN!
[FireTV] AUTH → Signature sent (after confirmation).
[FireTV] CNXN: device::ro.product.name=...
[FireTV] ADB connected
[FireTV] Polling started (10000ms).
```

### Subsequent connects (saved key)
```
[FireTV] Initialize 192.168.1.x:5555 (key loading async...)
[FireTV] RSA key loaded: \user\FireTV_ADBKey.xml
[FireTV] Connecting 192.168.1.x:5555...
[FireTV] AUTH → Signature sent (256 bytes).
[FireTV] CNXN: device::ro.product.name=...
[FireTV] ADB connected
[FireTV] Polling started (10000ms).
```

---

## Notes

- Volume control targets `STREAM_MUSIC` (stream 3). This may not reflect
  HDMI ARC or optical passthrough volume depending on the audio output mode.
- Auto-reconnect is active while connected — the module retries every 10 seconds
  after an unexpected disconnect.
- `Input_Text$` escapes single quotes and replaces spaces with `%s` as required
  by the Android `input text` command.

---

## Versioning

| Type | Example | Trigger |
|---|---|---|
| Major | `2.0` | On request / breaking changes |
| Minor | `2.x` | New features / new signals |
| Bugfix | `2.x.x` | Bug fixes |
