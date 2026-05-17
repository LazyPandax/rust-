using RustPlusDesk.Services;

namespace RustPlusDesk.Tests;

public sealed class UpdateReleaseSourceTests : IDisposable
{
    private readonly string? _originalUpdateRepo = Environment.GetEnvironmentVariable("RUSTPLUSDESK_UPDATE_REPO");
    private readonly string? _originalGitHubRepo = Environment.GetEnvironmentVariable("RUSTPLUSDESK_GITHUB_REPO");

    [Fact]
    public void Load_DefaultsToPandaReleaseRepoWhenConfiguredRepoIsInvalid()
    {
        Environment.SetEnvironmentVariable("RUSTPLUSDESK_UPDATE_REPO", "not-a-github-repo");
        Environment.SetEnvironmentVariable("RUSTPLUSDESK_GITHUB_REPO", null);

        var source = UpdateReleaseSource.Load();

        Assert.Equal("LazyPandax/rust-", source.FullName);
        Assert.Equal("https://github.com/LazyPandax/rust-/releases/latest", source.ReleasesUrl);
    }

    [Fact]
    public void TryParse_RejectsEmptyInput()
    {
        var parsed = UpdateReleaseSource.TryParse("   ", out _);

        Assert.False(parsed);
    }

    [Theory]
    [InlineData("owner/repo", "owner/repo")]
    [InlineData("https://github.com/owner/repo", "owner/repo")]
    [InlineData("https://github.com/owner/repo.git", "owner/repo")]
    public void Load_ParsesConfiguredRepo(string configured, string expected)
    {
        Environment.SetEnvironmentVariable("RUSTPLUSDESK_UPDATE_REPO", configured);
        Environment.SetEnvironmentVariable("RUSTPLUSDESK_GITHUB_REPO", null);

        var source = UpdateReleaseSource.Load();

        Assert.Equal(expected, source.FullName);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("RUSTPLUSDESK_UPDATE_REPO", _originalUpdateRepo);
        Environment.SetEnvironmentVariable("RUSTPLUSDESK_GITHUB_REPO", _originalGitHubRepo);
    }
}
