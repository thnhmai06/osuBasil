using System.Text.Json;

namespace Basil.Application.Json;

/// <summary>
///     The one <see cref="JsonSerializerOptions" /> instance every live-payload serialization on the
///     `api.` host should use — <see cref="SnapshotChannel{T}" />/<c>JsonMergePatch</c> (full snapshots
///     and RFC 7396 deltas), the packet handlers that publish onto those same channels
///     (<c>MatchScoreUpdateHandler</c>/<c>SpectateFramesHandler</c>), and every match sub-resource
///     route's SSE payload. Web-style camelCase naming plus <see cref="CountryJsonConverter" /> —
///     matching what <c>Program.cs</c>'s <c>ConfigureHttpJsonOptions</c> configures for regular JSON
///     responses, since <see cref="Microsoft.AspNetCore.Http.Json.JsonOptions.SerializerOptions" /> has
///     no public setter to point at this instance directly, <c>Program.cs</c> copies this instance's
///     converters onto ASP.NET Core's own options instead — see its own doc comment.
/// </summary>
public static class BasilJsonOptions
{
    public static readonly JsonSerializerOptions Instance = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new CountryJsonConverter());
        return options;
    }
}
