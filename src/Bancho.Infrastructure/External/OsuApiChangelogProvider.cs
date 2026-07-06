using System.Text.Json;
using Bancho.Application.Abstractions;
using Bancho.Domain;

namespace Bancho.Infrastructure.External;

/// <inheritdoc cref="IOsuVersionAllowlistProvider" />
public sealed class OsuApiChangelogProvider(HttpClient httpClient) : IOsuVersionAllowlistProvider
{
    private const string ChangelogUrl = "https://osu.ppy.sh/api/v2/changelog";

    public async Task<IReadOnlySet<DateOnly>?> GetAllowedVersionsAsync(OsuStream stream, CancellationToken cancellationToken = default)
    {
        var streamParam = StreamParam(stream);
        var response = await httpClient.GetAsync($"{ChangelogUrl}?stream={streamParam}", cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(cancellationToken));
        var allowed = new HashSet<DateOnly>();

        foreach (var build in document.RootElement.GetProperty("builds").EnumerateArray())
        {
            var versionText = build.GetProperty("version").GetString()!;
            var version = new DateOnly(
                int.Parse(versionText[..4]),
                int.Parse(versionText[4..6]),
                int.Parse(versionText[6..8]));
            allowed.Add(version);

            var isMajor = build.GetProperty("changelog_entries").EnumerateArray()
                .Any(entry => entry.TryGetProperty("major", out var majorProp) && majorProp.GetBoolean());
            if (isMajor)
            {
                // this build is a major iteration; don't allow anything older.
                break;
            }
        }

        return allowed;
    }

    private static string StreamParam(OsuStream stream) => stream switch
    {
        OsuStream.Stable => "stable40",
        OsuStream.Beta => "beta40",
        OsuStream.CuttingEdge => "cuttingedge",
        OsuStream.Tourney => "tourney",
        OsuStream.Dev => "dev",
        _ => throw new ArgumentOutOfRangeException(nameof(stream), stream, null),
    };
}
