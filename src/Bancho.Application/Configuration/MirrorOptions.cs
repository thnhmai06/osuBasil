namespace Bancho.Application.Configuration;

/// <summary>
///     Ports MIRROR_DOWNLOAD_ENDPOINT from app/settings.py. MIRROR_SEARCH_ENDPOINT is not ported —
///     osu-search.php now queries the local `maps` table instead of proxying a mirror (this server
///     runs fully offline). DownloadEndpoint is optional and unset by default: this server has no
///     local .osz file storage, so /d/{set_id} only works if an operator configures their own mirror
///     (local or otherwise) here; unset, it reports the download as unavailable rather than reaching
///     out to the internet.
/// </summary>
public sealed class MirrorOptions
{
    public const string SectionName = "Mirror";

    public string? DownloadEndpoint { get; init; }
}