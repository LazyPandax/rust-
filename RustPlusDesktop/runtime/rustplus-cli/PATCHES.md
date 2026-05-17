# RustPlus CLI Runtime Patches

This folder vendors the Rust+ CLI dependency chain because the upstream packages
still pull deprecated and vulnerable transitive dependencies.

Local changes:

- `@liamcottle/rustplus.js` is installed from `vendor/rustplus.js`.
- `@liamcottle/push-receiver` is installed from `vendor/push-receiver`.
- Deprecated `request` and `request-promise` calls were replaced with an axios
  wrapper in the vendored push receiver.
- `jimp` was replaced with `pngjs` for camera PNG rendering.
- `package.json` pins security-sensitive transitive packages through npm
  overrides.

Keep `package-lock.json`, `node_modules`, and `runtime/rustplus-cli.zip` in sync
after changing this folder because the desktop app ships the zipped CLI runtime.
