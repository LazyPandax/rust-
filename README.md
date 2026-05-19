![RustPlusDesk headline](RustPlusDesktop/Assets/Images/headlineGIT.jpg)

[![Discord](https://img.shields.io/badge/Discord-Rust%5E2%20%7C%20Rust%2B%20Desktop-5865f2?style=for-the-badge&logo=discord&logoColor=white)](https://discord.gg/G5TVPsqXQq)

# RustPlusDesk - Panda Edition

RustPlusDesk is an unofficial Windows desktop app for Rust+ Companion.

It helps you pair Rust servers, monitor events, control Smart Devices, view team chat, track players, and work with the live map from your PC.

This fork is maintained by Panda at [LazyPandax/rust-](https://github.com/LazyPandax/rust-) and uses GitHub Releases for update metadata and installer downloads.

[![Download latest installer](https://img.shields.io/badge/Download-Latest%20Installer-2ea44f?style=for-the-badge&logo=github)](https://github.com/LazyPandax/rust-/releases/latest/download/RustPlusDesk-Setup.exe)

[Open releases page](https://github.com/LazyPandax/rust-/releases/latest)

> This project is not affiliated with Facepunch Studios or Rust.

## Project Layout

- `RustPlusDesktop/` - WPF desktop application.
- `RustPlusDesk.Tests/` - xUnit tests for security and update helpers.
- `RustPlusDesktop/runtime/rustplus-cli/` - patched Rust+ CLI source and lockfile.
- `RustPlusDesktop/runtime/rustplus-cli.zip` - shipped CLI runtime archive.
- `docs/` - architecture notes, security notes, and project audit.

## Development

Use the .NET SDK version pinned in `global.json`:

```powershell
dotnet restore RustPlusDesk.sln
dotnet test RustPlusDesk.sln --configuration Release
```

For the Rust+ CLI runtime:

```powershell
cd RustPlusDesktop\runtime\rustplus-cli
npm ci --omit=dev
npm test
npm audit --omit=dev
cd ..\..\..
.\scripts\package-rustplus-cli.ps1 -SkipInstall -SkipTest -SkipAudit
```

See `docs/project-audit.md` for the current cleanup recommendations.
