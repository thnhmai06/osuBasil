using System.Text.RegularExpressions;

namespace Basil.Domain.Login;

/// <summary>Ported from app/objects/player.py's OsuStream (StrEnum).</summary>
public enum OsuStream : byte
{
    Stable,
    Beta,
    CuttingEdge,
    Tourney,
    Dev
}

public sealed partial record OsuVersion(DateOnly Date, int? Revision, OsuStream Stream)
{
    [GeneratedRegex(@"^b(?<date>\d{8})(?:\.(?<revision>\d))?(?<stream>beta|cuttingedge|dev|tourney)?$")]
    private static partial Regex VersionPattern();

    public static OsuVersion From(string osuVersionString)
    {
        var match = VersionPattern().Match(osuVersionString);
        if (!match.Success) throw new ArgumentException($"Invalid client version: {osuVersionString}");

        var dateText = match.Groups["date"].Value;
        var date = DateOnly.ParseExact(dateText, "yyyyMMdd");

        var revisionGroup = match.Groups["revision"];
        int? revision = revisionGroup.Success ? int.Parse(revisionGroup.Value) : null;

        var streamGroup = match.Groups["stream"];
        var stream = streamGroup.Success ? Enum.Parse<OsuStream>(streamGroup.Value, true) : OsuStream.Stable;

        return new OsuVersion(date, revision, stream);
    }
}