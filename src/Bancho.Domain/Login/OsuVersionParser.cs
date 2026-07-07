using System.Text.RegularExpressions;

namespace Bancho.Domain.Login;

/// <summary>Ported from app/constants/regexes.py's OSU_VERSION + app/api/domains/cho.py's parse_osu_version_string.</summary>
public static partial class OsuVersionParser
{
    [GeneratedRegex(@"^b(?<date>\d{8})(?:\.(?<revision>\d))?(?<stream>beta|cuttingedge|dev|tourney)?$")]
    private static partial Regex VersionRegex();

    public static OsuVersion? Parse(string osuVersionString)
    {
        var match = VersionRegex().Match(osuVersionString);
        if (!match.Success) return null;

        var dateText = match.Groups["date"].Value;
        var date = new DateOnly(
            int.Parse(dateText[..4]),
            int.Parse(dateText[4..6]),
            int.Parse(dateText[6..8]));

        var revisionGroup = match.Groups["revision"];
        int? revision = revisionGroup.Success ? int.Parse(revisionGroup.Value) : null;

        var streamGroup = match.Groups["stream"];
        var stream = streamGroup.Success ? ParseStream(streamGroup.Value) : OsuStream.Stable;

        return new OsuVersion(date, revision, stream);
    }

    private static OsuStream ParseStream(string value)
    {
        return value switch
        {
            "beta" => OsuStream.Beta,
            "cuttingedge" => OsuStream.CuttingEdge,
            "dev" => OsuStream.Dev,
            "tourney" => OsuStream.Tourney,
            _ => throw new ArgumentOutOfRangeException(nameof(value), value,
                "unreachable: constrained by regex alternation")
        };
    }
}