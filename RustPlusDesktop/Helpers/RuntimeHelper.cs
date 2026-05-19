using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Text.Json;

namespace RustPlusDesk.Helpers
{
    public static class RuntimeHelper
    {
        public static IReadOnlyList<string> GetAppBaseCandidates()
        {
            var raw = new List<string?>();

            try
            {
                var processPath = Environment.ProcessPath;
                if (!string.IsNullOrWhiteSpace(processPath))
                    raw.Add(Path.GetDirectoryName(processPath));
            }
            catch { }

            raw.Add(AppContext.BaseDirectory);
            raw.Add(AppDomain.CurrentDomain.BaseDirectory);
            raw.Add(Directory.GetCurrentDirectory());
            raw.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "RustPlusDesk"));

            var result = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var candidate in raw)
            {
                AddNormalized(candidate, result, seen);
            }

            return result;
        }

        public static string GetPrimaryAppBaseDirectory()
        {
            var candidates = GetAppBaseCandidates();
            return candidates.Count > 0 ? candidates[0] : AppContext.BaseDirectory;
        }

        public static string? FindBundledNode()
        {
            foreach (var baseDir in GetAppBaseCandidates())
            {
                foreach (var nodePath in GetNodeCandidates(baseDir))
                {
                    if (File.Exists(nodePath)) return nodePath;
                }
            }

            var pathValue = Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (var entry in pathValue.Split(Path.PathSeparator))
            {
                if (string.IsNullOrWhiteSpace(entry)) continue;
                try
                {
                    var nodePath = Path.Combine(entry.Trim(), "node.exe");
                    if (File.Exists(nodePath)) return nodePath;
                }
                catch { }
            }

            return null;
        }

        public static string GetNodeNotFoundMessage()
        {
            var msg = "Node.js runtime not found. Pairing cannot start without runtime\\node-win-x64\\node.exe.\n\nSearched locations:";

            foreach (var baseDir in GetAppBaseCandidates())
            {
                foreach (var nodePath in GetNodeCandidates(baseDir))
                    msg += "\n- " + nodePath;
            }

            msg += "\n\nRun the latest RustPlusDesk installer again. Chrome or Edge is only needed after the bundled Node runtime starts.";
            return msg;
        }

        public static string EnsureCliUnpackedRoot()
        {
            var target = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                                      "RustPlusDesk", "runtime", "rustplus-cli");
            Directory.CreateDirectory(target);

            var zip = FindRuntimeFile("runtime", "rustplus-cli.zip");
            if (zip != null)
            {
                var stamp = Path.Combine(target, ".stamp");
                var sig = $"{new FileInfo(zip).Length}-{File.GetLastWriteTimeUtc(zip).Ticks}";
                var need = !File.Exists(stamp) || File.ReadAllText(stamp) != sig
                           || !Directory.Exists(Path.Combine(target, "node_modules"));

                if (need)
                {
                    try { Directory.Delete(target, true); } catch { }
                    Directory.CreateDirectory(target);
                    ZipFile.ExtractToDirectory(zip, target);
                    File.WriteAllText(stamp, sig);
                }

                // The shipped zip can contain vendored packages (runtime/rustplus-cli/vendor/*)
                // without preserving NTFS junctions inside node_modules (reparse points are not
                // reliably round-trippable through zip). Ensure Node can resolve these packages.
                RepairVendoredNodeModules(target);
                return target;
            }

            foreach (var baseDir in GetAppBaseCandidates())
            {
                try
                {
                    var dev = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "runtime", "rustplus-cli"));
                    if (IsValidCliRoot(dev))
                    {
                        RepairVendoredNodeModules(dev);
                        return dev;
                    }
                }
                catch { }
            }

            if (IsValidCliRoot(target))
            {
                RepairVendoredNodeModules(target);
                return target;
            }

            throw new FileNotFoundException("rustplus-cli not found. Reinstall RustPlusDesk so runtime\\rustplus-cli.zip is present, or delete the cached runtime under %LOCALAPPDATA%\\RustPlusDesk\\runtime and start the app again.");
        }

        public static string? ResolveCliEntry(out string workingDir)
        {
            var root = EnsureCliUnpackedRoot();
            workingDir = root;

            foreach (var c in new[] {
                Path.Combine(root, "cli.js"),
                Path.Combine(root, "rustplus.js"),
                Path.Combine(root, "index.js"),
                Path.Combine(root, "node_modules", "@liamcottle", "rustplus.js", "cli", "index.js"),
                Path.Combine(root, "vendor", "rustplus.js", "cli", "index.js")
            })
            {
                if (File.Exists(c)) return c;
            }
            return null;
        }

        public static string? FindRustplusJsPackageRoot()
        {
            var root = EnsureCliUnpackedRoot();

            if (Directory.Exists(Path.Combine(root, "node_modules", "@liamcottle", "rustplus.js")))
                return root;

            return null;
        }

        private static string? FindRuntimeFile(params string[] relativeParts)
        {
            foreach (var baseDir in GetAppBaseCandidates())
            {
                var candidate = Combine(baseDir, relativeParts);
                if (File.Exists(candidate)) return candidate;
            }

            return null;
        }

        private static IEnumerable<string> GetNodeCandidates(string baseDir)
        {
            yield return Path.Combine(baseDir, "runtime", "node-win-x64", "node.exe");
            yield return Path.Combine(baseDir, "node-win-x64", "node.exe");
            yield return Path.Combine(baseDir, "node.exe");
        }

        private static string Combine(string baseDir, params string[] parts)
        {
            var path = baseDir;
            foreach (var part in parts)
                path = Path.Combine(path, part);
            return path;
        }

        private static bool IsValidCliRoot(string root)
        {
            try
            {
                return Directory.Exists(root)
                    && Directory.Exists(Path.Combine(root, "node_modules"))
                    && (File.Exists(Path.Combine(root, "node_modules", "@liamcottle", "rustplus.js", "cli", "index.js"))
                        || File.Exists(Path.Combine(root, "vendor", "rustplus.js", "cli", "index.js")));
            }
            catch
            {
                return false;
            }
        }

        private static void AddNormalized(string? path, List<string> result, HashSet<string> seen)
        {
            if (string.IsNullOrWhiteSpace(path)) return;

            try
            {
                var normalized = Path.GetFullPath(path);
                if (seen.Add(normalized))
                    result.Add(normalized);
            }
            catch { }
        }

        private static void RepairVendoredNodeModules(string cliRoot)
        {
            try
            {
                var vendorRoot = Path.Combine(cliRoot, "vendor");
                var nodeModulesRoot = Path.Combine(cliRoot, "node_modules");
                if (!Directory.Exists(vendorRoot) || !Directory.Exists(nodeModulesRoot)) return;

                var nodeModulesFull = Path.GetFullPath(nodeModulesRoot);
                if (!nodeModulesFull.EndsWith(Path.DirectorySeparatorChar))
                    nodeModulesFull += Path.DirectorySeparatorChar;

                foreach (var vendorPackageDir in Directory.GetDirectories(vendorRoot))
                {
                    var packageJson = Path.Combine(vendorPackageDir, "package.json");
                    if (!File.Exists(packageJson)) continue;

                    var name = TryReadPackageName(packageJson);
                    if (string.IsNullOrWhiteSpace(name)) continue;

                    // Scoped packages live under node_modules/@scope/name.
                    var relative = name!.Replace('/', Path.DirectorySeparatorChar);
                    var desiredPath = Path.GetFullPath(Path.Combine(nodeModulesRoot, relative));
                    if (!desiredPath.StartsWith(nodeModulesFull, StringComparison.OrdinalIgnoreCase)) continue;

                    // If the module is already present (real dir or junction), we're done.
                    if (File.Exists(Path.Combine(desiredPath, "package.json"))) continue;

                    SafeDeleteDirectory(desiredPath);
                    Directory.CreateDirectory(Path.GetDirectoryName(desiredPath)!);

                    if (!TryCreateJunction(desiredPath, vendorPackageDir))
                    {
                        // Fallback for systems where junction creation is blocked: copy the directory.
                        CopyDirectory(vendorPackageDir, desiredPath);
                    }
                }
            }
            catch
            {
                // best-effort; pairing should keep working even if this fails
            }
        }

        private static string? TryReadPackageName(string packageJsonPath)
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(packageJsonPath));
                if (doc.RootElement.TryGetProperty("name", out var nameEl) && nameEl.ValueKind == JsonValueKind.String)
                    return nameEl.GetString();
            }
            catch { }
            return null;
        }

        private static bool TryCreateJunction(string junctionPath, string targetPath)
        {
            try
            {
                if (!Directory.Exists(targetPath)) return false;

                // mklink requires the link path to not exist.
                SafeDeleteDirectory(junctionPath);

                var psi = new ProcessStartInfo("cmd.exe",
                    $"/c mklink /J \"{junctionPath}\" \"{targetPath}\"")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using var p = Process.Start(psi);
                if (p == null) return false;

                p.WaitForExit(5000);
                return p.ExitCode == 0 && Directory.Exists(junctionPath);
            }
            catch
            {
                return false;
            }
        }

        private static void SafeDeleteDirectory(string path)
        {
            if (!Directory.Exists(path)) return;

            try
            {
                var attrs = File.GetAttributes(path);
                if ((attrs & FileAttributes.ReparsePoint) != 0)
                {
                    // Remove the link itself; do not recurse into the target.
                    Directory.Delete(path);
                    return;
                }
            }
            catch { }

            try { Directory.Delete(path, recursive: true); } catch { }
        }

        private static void CopyDirectory(string sourceDir, string destinationDir)
        {
            Directory.CreateDirectory(destinationDir);

            foreach (var file in Directory.GetFiles(sourceDir))
            {
                var dest = Path.Combine(destinationDir, Path.GetFileName(file));
                File.Copy(file, dest, overwrite: true);
            }

            foreach (var dir in Directory.GetDirectories(sourceDir))
            {
                var dest = Path.Combine(destinationDir, Path.GetFileName(dir));
                CopyDirectory(dir, dest);
            }
        }
    }
}
