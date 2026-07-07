namespace Bancho.Domain.Login;

/// <summary>Ported from app/objects/player.py's WINE_ADAPTER_SENTINEL + app/api/domains/cho.py's parse_adapters_string.</summary>
public static class AdaptersStringParser
{
    public const string WineAdapterSentinel = "runningunderwine";

    public static (IReadOnlyList<string> Adapters, bool RunningUnderWine) Parse(string adaptersString)
    {
        if (adaptersString == WineAdapterSentinel)
        {
            return ([WineAdapterSentinel], true);
        }

        if (!adaptersString.EndsWith('.'))
        {
            throw new FormatException("adapter list is missing trailing delimiter");
        }

        return (adaptersString[..^1].Split('.'), false);
    }
}
