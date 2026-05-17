using System;
using System.IO;

namespace RustPlusDesk.Services;

internal sealed record UpdateReleaseSource(string Owner, string Name)
{
    private const string DefaultOwner = "LazyPandax";
    private const string DefaultName = "rust-";

    private static string AppDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RustPlusDesk");

    public static string ConfigPath => Path.Combine(AppDir, "github-release-repo.txt");

    public string FullName => $"{Owner}/{Name}";
    public string ReleasesUrl => $"https://github.com/{FullName}/releases/latest";

    public static UpdateReleaseSource Load()
    {
        var configured =
            Environment.GetEnvironmentVariable("RUSTPLUSDESK_UPDATE_REPO") ??
            Environment.GetEnvironmentVariable("RUSTPLUSDESK_GITHUB_REPO");

        if (string.IsNullOrWhiteSpace(configured) && File.Exists(ConfigPath))
        {
            try { configured = File.ReadAllText(ConfigPath); }
            catch { configured = null; }
        }

        return TryParse(configured, out var source)
            ? source
            : new UpdateReleaseSource(DefaultOwner, DefaultName);
    }

    internal static bool TryParse(string? value, out UpdateReleaseSource source)
    {
        source = new UpdateReleaseSource(DefaultOwner, DefaultName);
        if (string.IsNullOrWhiteSpace(value)) return false;

        var repo = value.Trim();

        if (Uri.TryCreate(repo, UriKind.Absolute, out var uri) &&
            uri.Host.EndsWith("github.com", StringComparison.OrdinalIgnoreCase))
        {
            repo = uri.AbsolutePath;
        }

        repo = repo.Trim().Trim('/');
        if (repo.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
            repo = repo[..^4];

        var parts = repo.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2) return false;

        source = new UpdateReleaseSource(parts[0], parts[1]);
        return !string.IsNullOrWhiteSpace(source.Owner) && !string.IsNullOrWhiteSpace(source.Name);
    }
}
