namespace OpenOsuTournament.Bancho.Application.Configuration;

/// <summary>
///     Ports REPLAYS_PATH from app/api/dependencies.py (".data/osr" relative to the process's
///     working directory). AvatarsPath/MapsetsPath/SeasonalsPath are new — bancho.py has no local
///     equivalent (it proxies avatars/beatmaps to osu.ppy.sh and has no seasonal-background storage
///     at all), added for this server's fully-offline file serving.
/// </summary>
public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    public string ReplaysPath { get; init; } = Path.Combine(".data", "osr");
    public string AvatarsPath { get; init; } = Path.Combine(".data", "avatars");
    public string MapsetsPath { get; init; } = Path.Combine(".data", "mapsets");
    public string SeasonalsPath { get; init; } = Path.Combine(".data", "seasonals");
}