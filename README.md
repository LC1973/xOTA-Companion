# xOTA Companion

A Windows desktop app for POTA and SOTA activators. Displays live spots from [POTAwatch](https://pota.app) and [SOTAwatch](https://sotawatch.sota.org.uk), integrates with [GreenLogger](https://www.greenlogger.com) for operator/radio management, supports CAT and TCI radio control, self-spotting, and interactive Mapbox maps.

---

## Download

Grab the latest **xOTACompanion.exe** from the [Releases](https://github.com/LC1973/xOTA-Companion/releases) page — it's a single self-contained executable, no installer needed.

---

## Requirements

| | |
|---|---|
| **OS** | Windows 10 (1803) or later, 64-bit |
| **Maps** | [Microsoft Edge WebView2 Runtime](https://developer.microsoft.com/en-us/microsoft-edge/webview2/) (usually already installed on Win 10/11) |
| **GreenLogger** | Optional — provides operator and radio profiles automatically |

---

## Getting Started

1. Download `xOTACompanion.exe` and place it anywhere (e.g. `C:\Ham\xOTACompanion\`).
2. Run it — on first launch it will detect GreenLogger if installed, or prompt you to configure your station manually via **Settings (⚙)**.
3. Spots refresh automatically every 2 minutes (configurable in Settings).

---

## Maps (Mapbox — optional)

Maps use the Mapbox GL JS API. You need a **free** Mapbox account and your own access token.

> **Note:** Mapbox tokens are personal and non-transferable. Each user must create their own free account.

### Setup

1. Sign up for a free account at [mapbox.com](https://www.mapbox.com/).
2. Go to [account.mapbox.com/access-tokens](https://account.mapbox.com/access-tokens) and copy your **Default public token** — it starts with `pk.`.
3. Set a **user environment variable** on your PC:
   - Open **Start → Search "environment variables" → Edit environment variables for your account**
   - Add: `XOTA_MAPBOX_TOKEN` = `pk.eyJ1...` (your token)
4. Restart xOTA Companion.

If no token is configured, clicking a spot's map button will show setup instructions.

---

## SOTA Self-Spotting

To post spots to SOTAwatch you need a SOTAwatch API key (a Keycloak Bearer token).

1. In xOTA Companion, open **Settings (⚙) → OPERATORS / SOTA KEYS**.
2. Select your callsign and click **Edit SOTA Key…**.
3. Click **Fetch…** and enter your SOTAwatch username and password to retrieve the token automatically.  
   The token is saved to your local config (`%APPDATA%\xOTA Companion\config.json`).

---

## Radio Control

Supports:
- **TCI** (Expert Electronics SDC / ExpertSDR) — configure host and port in Settings.
- **CAT** (CI-V serial / COM port) — configure COM port and baud rate in Settings.

Radio integration is used for automatic frequency/mode display alongside spots.

---

## GreenLogger Integration

If [GreenLogger](https://www.greenlogger.com) is installed, xOTA Companion reads operator and radio profiles directly from its SQLite database. This is read-only — no data is written to GreenLogger.

---

## Configuration

Settings are saved to:
```
%APPDATA%\xOTA Companion\config.json
```
Delete this file to reset all settings to defaults.

---

## Building from Source

Requirements: [.NET 8 SDK](https://dotnet.microsoft.com/download), Windows 10 SDK.

```powershell
dotnet publish xOTACompanion.csproj -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true
```

Or use the included `deploy.ps1` script (targets `C:\BuildCache\xOTACompanion\publish\`).

---

## Licence

MIT — see [LICENSE](LICENSE).
