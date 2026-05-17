using RustPlusDesk.Services;

namespace RustPlusDesk.Tests;

public sealed class SecurityHelperTests
{
    [Fact]
    public void Redact_RemovesKnownSecretShapes()
    {
        var input = """
            playerToken=123456 token:abc secret=hunter2
            {"deviceToken":"device-secret","password":"pw"}
            rustplus://server|steam|token-value
            ExponentPushToken[real-token]
            """;

        var redacted = SensitiveLogRedactor.Redact(input);

        Assert.DoesNotContain("123456", redacted);
        Assert.DoesNotContain("abc", redacted);
        Assert.DoesNotContain("hunter2", redacted);
        Assert.DoesNotContain("device-secret", redacted);
        Assert.DoesNotContain("token-value", redacted);
        Assert.DoesNotContain("real-token", redacted);
        Assert.Contains("[REDACTED]", redacted);
    }

    [Fact]
    public void SecretProtector_RoundTripsCurrentUserSecret()
    {
        const string secret = "steam-token-123";

        var protectedValue = SecretProtector.Protect(secret);
        var unprotectedValue = SecretProtector.Unprotect(protectedValue);

        Assert.NotEqual(secret, protectedValue);
        Assert.True(SecretProtector.IsProtectedValue(protectedValue));
        Assert.Equal(secret, unprotectedValue);
    }
}
