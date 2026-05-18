# RustPlus Desktop Refactor Plan

This plan keeps the app working while reducing the amount of logic tied directly
to `MainWindow`.

## Current State

- The active app is `RustPlusDesktop/RustPlusDesk.csproj`.
- The solution has a small xUnit test project in `RustPlusDesk.Tests`.
- Core folders already exist: `Models`, `Services`, `ViewModels`, `Views`,
  `Converters`, `Assets`, `Scripts`, and `Installer`.
- `MainWindow.xaml`, `MainWindow.xaml.cs`, and several partial classes still
  contain most of the UI behavior.
- The Rust+ CLI runtime is vendored under `RustPlusDesktop/runtime/rustplus-cli`
  and shipped as `runtime/rustplus-cli.zip`.

## Completed Cleanup

- Window XAML/code-behind files live under `Views/Windows`.
- Main window feature partials live under `Views/MainWindow`.
- Runtime services such as Rust+ communication, pairing, storage, tracking, and
  release lookup live under `Services`.
- The project file uses asset globs instead of a hand-maintained list of every
  icon and screenshot.
- Generated folders such as `node_modules` and design-tool backup folders are
  ignored instead of treated as source.
- `scripts/package-rustplus-cli.ps1` rebuilds the shipped CLI runtime archive,
  and CI now refreshes the archive before publishing the Windows app.

## Next Refactor Phases

### Phase 1: MainWindow Boundaries

1. Extract map state and operations into a `MapViewModel`.
2. Extract device list/toggle logic into a `DevicesViewModel`.
3. Extract team chat/member logic into a `TeamViewModel`.
4. Leave existing partial classes as thin event bridges during the transition.

### Phase 2: User Controls

1. Create `Views/Controls/MapView.xaml`.
2. Create `Views/Controls/DevicesView.xaml`.
3. Create `Views/Controls/TeamView.xaml`.
4. Move the corresponding XAML from `MainWindow.xaml` into those controls.

### Phase 3: Dependency Injection

1. Add a small service container in `App.xaml.cs`.
2. Register `IRustPlusClient`, `IPairingListener`, `StorageService`,
   `TrackingService`, and release/update services.
3. Pass services into view models through constructors.
4. Prefer interfaces at UI boundaries so unit tests can cover behavior without
   opening WPF windows.

### Phase 4: Runtime Packaging

1. Keep `runtime/rustplus-cli.zip` generated through
   `scripts/package-rustplus-cli.ps1`.
2. Download or cache the portable Node runtime in CI instead of tracking the
   full `runtime/node-win-x64` directory.
3. Keep only source, lock files, patches, and packaging scripts in Git.

## Priority Order

1. Map extraction, because it is the largest UI area and has the most behavior.
2. Device extraction, because it touches live server actions and needs tests.
3. Portable Node cleanup, because it will reduce repo size sharply.
4. Warning cleanup, because the baseline build works but still reports dead
   fields, unreachable code, and obsolete API usage.
