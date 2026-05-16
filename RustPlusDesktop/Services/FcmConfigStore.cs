using System;
using System.IO;

namespace RustPlusDesk.Services;

internal static class FcmConfigStore
{
    private static string AppDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RustPlusDesk");

    public static string PlainConfigPath => Path.Combine(AppDir, "rustplusjs-config.json");
    public static string ProtectedConfigPath => Path.Combine(AppDir, "rustplusjs-config.protected");

    public static bool IsConfigured
    {
        get
        {
            if (File.Exists(PlainConfigPath) && new FileInfo(PlainConfigPath).Length > 50)
                return true;

            return File.Exists(ProtectedConfigPath) && new FileInfo(ProtectedConfigPath).Length > 50;
        }
    }

    public static string? ReadConfigJson()
    {
        try
        {
            if (File.Exists(PlainConfigPath))
                return File.ReadAllText(PlainConfigPath);

            if (File.Exists(ProtectedConfigPath))
                return SecretProtector.Unprotect(File.ReadAllText(ProtectedConfigPath));
        }
        catch
        {
            return null;
        }

        return null;
    }

    public static void EnsurePlaintextForCli()
    {
        Directory.CreateDirectory(AppDir);
        if (File.Exists(PlainConfigPath)) return;

        var json = ReadConfigJson();
        if (!string.IsNullOrWhiteSpace(json))
            File.WriteAllText(PlainConfigPath, json);
    }

    public static void SaveConfigJson(string json, bool keepPlaintext)
    {
        Directory.CreateDirectory(AppDir);
        File.WriteAllText(ProtectedConfigPath, SecretProtector.Protect(json));

        if (keepPlaintext)
        {
            File.WriteAllText(PlainConfigPath, json);
        }
        else
        {
            TryDeletePlaintext();
        }
    }

    public static void ProtectPlaintextAtRest(bool deletePlaintext)
    {
        try
        {
            if (!File.Exists(PlainConfigPath)) return;

            var json = File.ReadAllText(PlainConfigPath);
            if (string.IsNullOrWhiteSpace(json)) return;

            SaveConfigJson(json, keepPlaintext: !deletePlaintext);
        }
        catch
        {
            // Best-effort protection. Callers should not fail pairing/listening because of storage cleanup.
        }
    }

    public static void DeleteAll()
    {
        TryDeletePlaintext();
        try
        {
            if (File.Exists(ProtectedConfigPath))
                File.Delete(ProtectedConfigPath);
        }
        catch
        {
            // best effort
        }
    }

    private static void TryDeletePlaintext()
    {
        try
        {
            if (File.Exists(PlainConfigPath))
                File.Delete(PlainConfigPath);
        }
        catch
        {
            // best effort
        }
    }
}
