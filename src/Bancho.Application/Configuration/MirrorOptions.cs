namespace Bancho.Application.Configuration;

/// <summary>Ports MIRROR_SEARCH_ENDPOINT / MIRROR_DOWNLOAD_ENDPOINT from app/settings.py.</summary>
public sealed class MirrorOptions
{
    public const string SectionName = "Mirror";

    public required string SearchEndpoint { get; init; }
    public required string DownloadEndpoint { get; init; }
}
