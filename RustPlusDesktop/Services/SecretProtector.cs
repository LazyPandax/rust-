using System;
using System.Security.Cryptography;
using System.Text;

namespace RustPlusDesk.Services;

internal static class SecretProtector
{
    private const string Prefix = "dpapi:v1:";
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("RustPlusDesk.LocalSecrets.v1");

    public static bool IsProtectedValue(string? value)
        => !string.IsNullOrWhiteSpace(value) && value.StartsWith(Prefix, StringComparison.Ordinal);

    public static string Protect(string? plaintext)
    {
        if (string.IsNullOrEmpty(plaintext)) return string.Empty;
        if (IsProtectedValue(plaintext)) return plaintext;

        var raw = Encoding.UTF8.GetBytes(plaintext);
        var protectedBytes = ProtectedData.Protect(raw, Entropy, DataProtectionScope.CurrentUser);
        return Prefix + Convert.ToBase64String(protectedBytes);
    }

    public static string Unprotect(string? protectedValue)
    {
        if (string.IsNullOrEmpty(protectedValue)) return string.Empty;
        if (!IsProtectedValue(protectedValue)) return protectedValue;

        var b64 = protectedValue[Prefix.Length..];
        var protectedBytes = Convert.FromBase64String(b64);
        var raw = ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.CurrentUser);
        return Encoding.UTF8.GetString(raw);
    }
}
