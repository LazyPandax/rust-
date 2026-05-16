# Security Verification Notes

Date: 2026-05-16

## Static Checks Run

- Checked for browser credential, wallet, clipboard, keylogging, Telegram/webhook, `curl`, `wget`, PowerShell, `eval`, `Function(`, and `atob` indicators in the new source.
- Checked for removed overlay sync hard-coded IP/secret indicators: `85.214`, `23c5a7dbf02b63543`, `OVERLAY_SYNC_BASEURL`, and `OVERLAY_SYNC_SECRET_HEX`.
- Checked for unsafe updater execution indicators: `Verb = "runas"` and old `Download and install now` wording.
- Checked for global Node process cleanup indicators: `GetProcessesByName("node")`.

## Results

- No browser credential theft, wallet theft, keylogger, clipboard monitor/replacer, Telegram webhook, or Discord webhook exfiltration logic was found in the edited new source.
- Remaining `curl`/PowerShell hits are in bundled Node runtime documentation/tooling, not the WPF application flow.
- Hard-coded public overlay sync IP and shared secret were removed from the app and helper scripts.
- Updater no longer starts downloaded installers as admin or automatically; it shows the downloaded path and SHA256.
- Main window close no longer kills all `node.exe`; pairing listener cleanup only stops app-owned child processes.

## Build Status

`dotnet build RustPlusDesk.sln` could not run in this environment because no .NET SDK is installed. `dotnet --info` reports .NET runtimes only.

Install the .NET 8 Windows Desktop SDK, then run:

```powershell
dotnet build RustPlusDesk.sln
```
