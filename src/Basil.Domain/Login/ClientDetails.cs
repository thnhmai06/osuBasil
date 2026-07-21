using System.Collections.Immutable;

namespace Basil.Domain.Login;

/// <summary>
///     Ported from app/objects/player.py's ClientDetails, scoped to what score submission's
///     validate_client_details needs. Captured once at login (see PlayerSession.Client) and
///     re-checked against every score submission from that session.
/// </summary>
public sealed record ClientDetails(
    string OsuPathMd5,
    string AdaptersMd5,
    string UninstallMd5,
    string DiskSignatureMd5,
    ImmutableList<string> Adapters)
{
    private const string WineAdapterSentinel = "runningunderwine";
    
    public bool IsRunningUnderWine => Adapters.Contains(WineAdapterSentinel) && Adapters.Count == 1;

    /// <summary>Ported from ClientDetails.client_hash (cached_property).</summary>
    public string Hash()
    {
        var adaptersString = string.Join('.', Adapters);
        if (adaptersString != WineAdapterSentinel) adaptersString += ".";

        return $"{OsuPathMd5}:{adaptersString}:{AdaptersMd5}:{UninstallMd5}:{DiskSignatureMd5}:";
    }
    
    public static ClientDetails From(string hash)
    {
        var hashParts = hash[..^1].Split(':', 5);
        
        var osuPathMd5 = hashParts[0];
        var adaptersString = hashParts[1];
        var adaptersMd5 = hashParts[2];
        var uninstallMd5 = hashParts[3];
        var diskSignatureMd5 = hashParts[4];
        
        var adapters = ParseAdapters(adaptersString);

        return new ClientDetails(osuPathMd5, adaptersMd5, uninstallMd5, diskSignatureMd5, adapters);
    }
    
    private static ImmutableList<string> ParseAdapters(string adaptersString)
    {
        if (adaptersString == WineAdapterSentinel) return [WineAdapterSentinel];
        return adaptersString.EndsWith('.')
            ? adaptersString[..^1].Split('.').ToImmutableList()
            : throw new FormatException("Adapter list is missing trailing delimiter");
    }
}