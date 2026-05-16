using System;
using System.IO;

namespace RustPlusDesk.Services;

internal static class GitHubTokenStore
{
    private static string AppDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RustPlusDesk");

    public static string PlainImportPath => Path.Combine(AppDir, "github-token.txt");
    public static string ProtectedTokenPath => Path.Combine(AppDir, "github-token.protected");

    public static string? ReadToken()
    {
        var envToken = Environment.GetEnvironmentVariable("RUSTPLUSDESK_GITHUB_TOKEN")
                       ?? Environment.GetEnvironmentVariable("GITHUB_TOKEN");
        if (!string.IsNullOrWhiteSpace(envToken))
            return NormalizeToken(envToken);

        var importedToken = TryImportPlaintextToken();
        if (!string.IsNullOrWhiteSpace(importedToken))
            return importedToken;

        try
        {
            if (!File.Exists(ProtectedTokenPath)) return null;

            var token = NormalizeToken(SecretProtector.Unprotect(File.ReadAllText(ProtectedTokenPath)));
            return string.IsNullOrWhiteSpace(token) ? null : token;
        }
        catch
        {
            return null;
        }
    }

    public static bool HasConfiguredToken()
        => !string.IsNullOrWhiteSpace(ReadToken());

    private static string? TryImportPlaintextToken()
    {
        try
        {
            if (!File.Exists(PlainImportPath)) return null;

            var token = NormalizeToken(File.ReadAllText(PlainImportPath));
            if (string.IsNullOrWhiteSpace(token)) return null;

            Directory.CreateDirectory(AppDir);
            File.WriteAllText(ProtectedTokenPath, SecretProtector.Protect(token));
            TryDeletePlaintext();
            return token;
        }
        catch
        {
            return null;
        }
    }

    private static void TryDeletePlaintext()
    {
        try
        {
            if (File.Exists(PlainImportPath))
                File.Delete(PlainImportPath);
        }
        catch
        {
            // Best effort: leaving the token usable is more important than failing update checks.
        }
    }

    private static string NormalizeToken(string? token)
    {
        var value = (token ?? string.Empty).Trim();

        const string bearerPrefix = "Bearer ";
        if (value.StartsWith(bearerPrefix, StringComparison.OrdinalIgnoreCase))
            value = value[bearerPrefix.Length..].Trim();

        const string tokenPrefix = "token ";
        if (value.StartsWith(tokenPrefix, StringComparison.OrdinalIgnoreCase))
            value = value[tokenPrefix.Length..].Trim();

        return value;
    }
}
