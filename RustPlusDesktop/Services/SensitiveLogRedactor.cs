using System.Text.RegularExpressions;

namespace RustPlusDesk.Services;

internal static class SensitiveLogRedactor
{
    private static readonly Regex ExpoPushTokenRegex =
        new(@"ExponentPushToken\[[^\]]+\]", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex JsonSecretRegex =
        new(@"(""(?:deviceToken|playerToken|token|secret|password)""\s*:\s*"")[^""]+("")",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex KeyValueSecretRegex =
        new(@"((?:playerToken|deviceToken|token|secret|password)\s*[=:]\s*)[^\s,;]+",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex RustPlusTokenUrlRegex =
        new(@"(rustplus://[^|""'\s]*\|[^|""'\s]*\|)[^|""'\s]+",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static string Redact(string? input)
    {
        if (string.IsNullOrEmpty(input)) return string.Empty;

        var s = ExpoPushTokenRegex.Replace(input, "ExponentPushToken[REDACTED]");
        s = JsonSecretRegex.Replace(s, "$1[REDACTED]$2");
        s = KeyValueSecretRegex.Replace(s, "$1[REDACTED]");
        s = RustPlusTokenUrlRegex.Replace(s, "$1[REDACTED]");
        return s;
    }
}
