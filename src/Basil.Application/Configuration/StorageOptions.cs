namespace Basil.Application.Configuration;

/// <summary>
///     Local file storage folder names. Fixed, not configurable via Settings.toml/env — Infrastructure's
///     DI composition root resolves each of these against the executable's directory (not the
///     process's working directory) and constructs this POCO directly, it is not bound from
///     IConfiguration. AvatarsPath/MapsetsPath/SeasonalsPath/FaqsPath have no bancho.py equivalent
///     (it proxies avatars/beatmaps to osu.ppy.sh, has no seasonal-background storage, and its
///     `!faq` entries are hardcoded server-side, not read from files) — these were added for this
///     server's fully-offline file serving.
/// </summary>
public sealed class StorageOptions
{
    public required string ReplaysPath { get; init; }
    public required string AvatarsPath { get; init; }
    public required string MapsetsPath { get; init; }
    public required string SeasonalsPath { get; init; }
    public required string FaqsPath { get; init; }
}
