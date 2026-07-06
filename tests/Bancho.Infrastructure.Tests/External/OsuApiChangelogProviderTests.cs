using System.Net;
using Bancho.Domain;
using Bancho.Infrastructure.External;

namespace Bancho.Infrastructure.Tests.External;

/// <summary>Ported from app/api/domains/cho.py's get_allowed_client_versions (osu!api v2 changelog).</summary>
public class OsuApiChangelogProviderTests
{
    private sealed class FakeHandler(HttpStatusCode statusCode, string body) : HttpMessageHandler
    {
        public Uri? LastRequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequestUri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(statusCode) { Content = new StringContent(body) });
        }
    }

    [Fact]
    public async Task GetAllowedVersions_StopsAtFirstMajorBuild()
    {
        // newest first; the 2nd build has a major changelog entry, so anything older than it
        // (the 3rd build) must be excluded from the allowlist.
        var body = """
            {
              "builds": [
                { "version": "20240301", "changelog_entries": [{ "major": false }] },
                { "version": "20240201", "changelog_entries": [{ "major": true }] },
                { "version": "20240101", "changelog_entries": [{ "major": false }] }
              ]
            }
            """;
        var provider = new OsuApiChangelogProvider(new HttpClient(new FakeHandler(HttpStatusCode.OK, body)));

        var allowed = await provider.GetAllowedVersionsAsync(OsuStream.Stable);

        Assert.NotNull(allowed);
        Assert.Contains(new DateOnly(2024, 3, 1), allowed!);
        Assert.Contains(new DateOnly(2024, 2, 1), allowed);
        Assert.DoesNotContain(new DateOnly(2024, 1, 1), allowed);
    }

    [Fact]
    public async Task GetAllowedVersions_HttpFailure_ReturnsNull()
    {
        var provider = new OsuApiChangelogProvider(new HttpClient(new FakeHandler(HttpStatusCode.ServiceUnavailable, "")));

        Assert.Null(await provider.GetAllowedVersionsAsync(OsuStream.Stable));
    }

    [Theory]
    [InlineData(OsuStream.Stable, "stable40")]
    [InlineData(OsuStream.Beta, "beta40")]
    [InlineData(OsuStream.CuttingEdge, "cuttingedge")]
    [InlineData(OsuStream.Tourney, "tourney")]
    public async Task GetAllowedVersions_RequestsCorrectStreamParameter(OsuStream stream, string expectedStreamParam)
    {
        var handler = new FakeHandler(HttpStatusCode.OK, """{ "builds": [] }""");
        var provider = new OsuApiChangelogProvider(new HttpClient(handler));

        await provider.GetAllowedVersionsAsync(stream);

        Assert.Contains($"stream={expectedStreamParam}", handler.LastRequestUri!.ToString());
    }
}
