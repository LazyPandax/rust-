# Old Rust++ Feature Matrix

Compared:

- Old source: `C:\Users\panda\Desktop\rust +\old RUST++\rustplusplus`
- New target: `C:\Users\panda\Desktop\rust +\rustplus-desktop-v5.0.1-source`

Static review date: 2026-05-16.

## Security-First Findings

| Area | Old path | Decision | Reason |
| --- | --- | --- | --- |
| Default Discord bot token | `old RUST++\rustplusplus\config\index.js` | Skip | Contains a real-looking fallback Discord bot token string. Do not port. Require user-supplied secrets only. |
| npm preinstall hook | `old RUST++\rustplusplus\package.json` | Skip | `preinstall` runs `npx npm-force-resolutions`; avoid bringing install-time script execution into the desktop app. |
| Discord bot runtime | `old RUST++\rustplusplus\index.ts`, `src\structures\DiscordBot.js` | Skip for now | Useful but high-permission and token-bearing. Port only as an explicit opt-in integration with DPAPI-protected token storage. |
| External credential app flow | `old RUST++\rustplusplus\docs\credentials.md` | Skip | New app already owns pairing. Do not add old external credential tooling. |

## Feature Matrix

| Feature | Old implementation | New app state | Decision | Notes |
| --- | --- | --- | --- | --- |
| Pairing / FCM listener | `src\util\FcmListener*.js` | Native WPF wrapper around bundled rustplus CLI | Merge both | New app keeps CLI architecture; added visible Edge login, redacted logs, clearer Expo/Chrome errors, and protected config at rest. |
| Smart switches | `src\handlers\smartSwitchHandler.js`, `src\commands\switch.js` | Present in Devices UI | Keep new | New UI is a better fit than Discord command port. |
| Smart switch groups | `src\handlers\smartSwitchGroupHandler.js` | Present in Devices UI | Keep new | Existing WPF grouping is retained. |
| Smart alarms | `src\handlers\smartAlarmHandler.js`, `docs\smart_devices.md` | Present with alarm notifications | Keep new | Existing alarm flow retained; log redaction now covers notification paths. |
| Storage monitors | `src\handlers\storageMonitorHandler.js`, `src\commands\storagemonitor.js` | Present in Devices UI | Keep new | Existing device import/export now avoids unsafe remote sync by default. |
| Team chat bridge | `src\handlers\teamChatHandler.js` | Present as in-app team chat | Merge later | Discord bridge skipped until an opt-in Discord integration exists. |
| In-game chat commands | `src\handlers\inGameCommandHandler.js`, `docs\full_list_features.md` | Partial support in `Views\MainWindow\Map\MainWindow.Map.ChatCommands.cs` | Merge later | New command engine should port commands one at a time with tests. |
| Battlemetrics trackers | `src\structures\Battlemetrics.js`, `src\handlers\battlemetricsHandler.js` | Present in tracking/player views | Keep new | New app already has tracking services and UI. |
| Market/vending search | `src\commands\market.js`, `src\handlers\vendingMachineHandler.js` | Present as Shop Search / PathFinder | Keep new | New app already provides richer map UI. |
| Item info / craft / recycle / research / decay / upkeep | `src\commands\item.js`, `craft.js`, `recycle.js`, `research.js`, `decay.js`, `upkeep.js` | Rust++ Tools window | Ported | Imported the static Rust++ data into a read-only desktop tools window. The Discord command layer was not ported. |
| CCTV codes | `src\commands\cctv.js`, `src\staticFiles\cctv.json` | Rust++ Tools window | Ported | Static monument camera code lookup is available without Discord or bot credentials. |
| Event notifications | `src\structures\MapMarkers.js`, `src\rustplusEvents\message.js` | Present for cargo/heli/chinook/oil/deep sea markers | Keep new | New app has map-centric notifications. |
| Timers / notes | Old in-game commands and docs | Not a primary new UI feature | Port later | Low risk; good candidate after security fixes. |
| Translation / TTS / Discord voice | `translate`, `src\commands\voice.js`, `src\discordTools\discordVoice.js` | Not present | Skip | Adds external services, voice dependencies, and Discord permissions. |
| Discord slash commands / channels / roles | `src\commands\*.js`, `src\discordTools\*.js` | Not present | Skip for now | Large security boundary: Discord bot token, permissions, message ingestion, and user identity mapping. |
| Multi-language text | `src\languages\*.json` | New app mostly hard-coded/mixed English/German | Port later | Useful, but requires a deliberate localization pass. |

## Porting Order

1. Security/stability fixes: completed in this branch for pairing logs, Edge login, DPAPI-backed profile/FCM config storage, updater behavior, overlay sync defaults, and scoped process cleanup.
2. Low-risk calculators: item, craft, recycle, research, decay, upkeep. Completed in `Rust++ Tools`.
3. Low-risk utility UI: CCTV static lookup is completed in `Rust++ Tools`; timers and notes remain future candidates.
4. Higher-risk integrations: Discord bridge, slash commands, roles, voice/TTS. These stay skipped until the app has explicit opt-in settings and protected token storage for Discord credentials.
