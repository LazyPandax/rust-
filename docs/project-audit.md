# Project Audit

Audit date: 2026-05-18

## Baseline

- Active repository: `rustplus-desktop-v5.0.1-source`
- Active solution: `RustPlusDesk.sln`
- Active app project: `RustPlusDesktop/RustPlusDesk.csproj`
- Test project: `RustPlusDesk.Tests/RustPlusDesk.Tests.csproj`
- Local SDK command: `.\.dotnet\dotnet.exe`
- Test result: 7 passed, 0 failed
- Build warning baseline: 28 warnings
- NuGet vulnerability scan: no vulnerable packages
- NuGet outdated scan: no package updates available
- npm audit for `RustPlusDesktop/runtime/rustplus-cli`: no vulnerabilities

## What Is Organized Now

- `.gitignore` covers local SDK/tool downloads, build outputs, test results,
  generated Node dependencies, and design-tool backup folders.
- The WPF project file is grouped by concern: properties, assets, runtime
  packaging, packages, and build settings.
- Empty/unused stub source files were removed.
- The outdated `coverlet.collector` test package was upgraded from `10.0.0` to
  `10.0.1`.
- `docs/refactor_plan.md` has been rewritten as a current, readable roadmap.
- `scripts/package-rustplus-cli.ps1` rebuilds `runtime/rustplus-cli.zip` from a
  clean dependency install, and CI runs it before publishing the installer.

## Recommended Adds

- Add real view models for map, devices, and team features.
- Add unit tests around device import/export, release lookup, and update
  verification.
- Add a small contributor guide that explains the local `.dotnet` SDK and Node
  runtime workflow.
- Add a workspace-level archive policy: keep `old RUST++` as read-only history,
  use `rustplus-desktop-v5.0.1-source` as the active repo, and remove the empty
  top-level `RustPlusDesktop` folder after confirming nothing external uses it.

## Recommended Upgrades

- Replace deprecated `RustPlusLegacy` usage in `RustPlusClientReal`.
- Move portable Node from checked-in runtime files to a CI download/cache step.
- Consider enabling `TreatWarningsAsErrors` only after the current warning list
  is cleaned up.

## Recommended Optimizations

- Split `RustPlusClientReal.cs`, `MainWindow.xaml`, and `MainWindow.xaml.cs`
  first; each is over 4,000 lines.
- Keep `node_modules` out of Git and rely on `npm ci --omit=dev`.
- Remove unused screenshots/backups from release packaging.
- Fix the unreachable code in
  `Views/MainWindow/Map/MainWindow.Map.ShopSearch.cs`.
- Remove or wire up unused fields reported by the Release build before turning
  warnings into errors.
- Convert repeated `async void` handlers into commands or task-returning
  helpers where possible.
- Replace broad `catch (Exception)` blocks with narrower handling in connection,
  pairing, and update code.
