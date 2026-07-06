namespace Bancho.Domain;

/// <summary>
/// Ported from app/objects/player.py's ClientDetails, scoped to what score submission's
/// validate_client_details needs. Captured once at login (see PlayerSession.Client) and
/// re-checked against every score submission from that session.
/// </summary>
public sealed class ClientDetails(
    DateOnly osuVersionDate,
    string osuPathMd5,
    string adaptersMd5,
    string uninstallMd5,
    string diskSignatureMd5,
    IReadOnlyList<string> adapters)
{
    public DateOnly OsuVersionDate { get; } = osuVersionDate;
    public string OsuPathMd5 { get; } = osuPathMd5;
    public string AdaptersMd5 { get; } = adaptersMd5;
    public string UninstallMd5 { get; } = uninstallMd5;
    public string DiskSignatureMd5 { get; } = diskSignatureMd5;
    public IReadOnlyList<string> Adapters { get; } = adapters;

    /// <summary>Ported from ClientDetails.client_hash (cached_property).</summary>
    public string ClientHash
    {
        get
        {
            var adaptersString = string.Join('.', Adapters);
            if (adaptersString != AdaptersStringParser.WineAdapterSentinel)
            {
                adaptersString += ".";
            }

            return $"{OsuPathMd5}:{adaptersString}:{AdaptersMd5}:{UninstallMd5}:{DiskSignatureMd5}:";
        }
    }
}
