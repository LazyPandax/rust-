using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using RustPlusDesk.Models;

namespace RustPlusDesk.Services;

public static class StorageService
{
    private static string AppDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RustPlusDesk");

    private static string ProfilesPath => Path.Combine(AppDir, "profiles.json");
    private static string ProfileSecretsPath => Path.Combine(AppDir, "profile-secrets.json");

    public static void SaveProfiles(IEnumerable<ServerProfile> profiles)
    {
        Directory.CreateDirectory(AppDir);
        var profileList = profiles.ToList();
        SaveProfileSecrets(profileList);

        var publicProfiles = CloneProfiles(profileList);
        foreach (var profile in publicProfiles)
            profile.PlayerToken = "";

        var json = JsonSerializer.Serialize(publicProfiles, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(ProfilesPath, json);
    }
    public static string GetProfilesPath() =>
    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RustPlusDesk", "profiles.json");

    public static List<ServerProfile> LoadProfiles()
    {
        if (!File.Exists(ProfilesPath)) return new List<ServerProfile>();
        try
        {
            var json = File.ReadAllText(ProfilesPath);
            var data = JsonSerializer.Deserialize<List<ServerProfile>>(json);
            data ??= new List<ServerProfile>();
            if (HydrateAndMigrateProfileSecrets(data))
                SaveProfiles(data);
            return data;
        }
        catch (Exception ex)
        {
            // ggf. mal ausgeben:
            Console.WriteLine("LoadProfiles-Fehler: " + ex);
            return new List<ServerProfile>();
        }
    }

    private static List<ServerProfile> CloneProfiles(IEnumerable<ServerProfile> profiles)
    {
        var json = JsonSerializer.Serialize(profiles);
        return JsonSerializer.Deserialize<List<ServerProfile>>(json) ?? new List<ServerProfile>();
    }

    private static string ProfileSecretKey(ServerProfile profile)
        => $"{profile.SteamId64}|{profile.Host}|{profile.Port}";

    private static Dictionary<string, string> LoadProfileSecrets()
    {
        try
        {
            if (!File.Exists(ProfileSecretsPath)) return new Dictionary<string, string>();
            var json = File.ReadAllText(ProfileSecretsPath);
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json)
                   ?? new Dictionary<string, string>();
        }
        catch
        {
            return new Dictionary<string, string>();
        }
    }

    private static void SaveProfileSecrets(IEnumerable<ServerProfile> profiles)
    {
        var secrets = LoadProfileSecrets();
        foreach (var profile in profiles)
        {
            if (string.IsNullOrWhiteSpace(profile.PlayerToken)) continue;
            secrets[ProfileSecretKey(profile)] = SecretProtector.Protect(profile.PlayerToken);
        }

        Directory.CreateDirectory(AppDir);
        var json = JsonSerializer.Serialize(secrets, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(ProfileSecretsPath, json);
    }

    private static bool HydrateAndMigrateProfileSecrets(List<ServerProfile> profiles)
    {
        var changed = false;
        var secrets = LoadProfileSecrets();

        foreach (var profile in profiles)
        {
            var key = ProfileSecretKey(profile);
            if (!string.IsNullOrWhiteSpace(profile.PlayerToken))
            {
                if (SecretProtector.IsProtectedValue(profile.PlayerToken))
                    profile.PlayerToken = SecretProtector.Unprotect(profile.PlayerToken);

                secrets[key] = SecretProtector.Protect(profile.PlayerToken);
                changed = true;
                continue;
            }

            if (secrets.TryGetValue(key, out var protectedToken))
                profile.PlayerToken = SecretProtector.Unprotect(protectedToken);
        }

        if (changed)
        {
            Directory.CreateDirectory(AppDir);
            var json = JsonSerializer.Serialize(secrets, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(ProfileSecretsPath, json);
        }

        return changed;
    }

    private static string CacheDir => Path.Combine(AppDir, "cache");

    public static void SaveCache<T>(string key, T data)
    {
        try
        {
            Directory.CreateDirectory(CacheDir);
            var path = Path.Combine(CacheDir, key + ".json");
            var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"SaveCache Error ({key}): {ex.Message}");
        }
    }

    public static T? LoadCache<T>(string key)
    {
        try
        {
            var path = Path.Combine(CacheDir, key + ".json");
            if (!File.Exists(path)) return default;
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<T>(json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"LoadCache Error ({key}): {ex.Message}");
            return default;
        }
    }
}
