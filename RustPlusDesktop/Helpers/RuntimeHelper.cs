using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

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
                return target;
            }

            foreach (var baseDir in GetAppBaseCandidates())
            {
                try
                {
                    var dev = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "runtime", "rustplus-cli"));
                    if (Directory.Exists(dev)) return dev;
                }
                catch { }
            }

            throw new FileNotFoundException("rustplus-cli not found. Reinstall RustPlusDesk so runtime\\rustplus-cli.zip is present.");
        }

        public static string? ResolveCliEntry(out string workingDir)
        {
            var root = EnsureCliUnpackedRoot();
            workingDir = root;

            foreach (var c in new[] {
                Path.Combine(root, "cli.js"),
                Path.Combine(root, "rustplus.js"),
                Path.Combine(root, "index.js"),
                Path.Combine(root, "node_modules", "@liamcottle", "rustplus.js", "cli", "index.js")
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
    }
}
