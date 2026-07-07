namespace Bancho.Application.Configuration;

/// <summary>
///     Ports bancho.py's app/settings_utils.py value parsing exactly, so environment-derived
///     configuration behaves identically to the Python server (including surprising cases like
///     treating "yes" as true and keeping trailing empty entries from a trailing comma).
/// </summary>
public static class EnvValueParser
{
    public static bool ReadBool(string value)
    {
        var lowered = value.ToLowerInvariant();
        return lowered is "true" or "1" or "yes";
    }

    public static IReadOnlyList<string> ReadList(string value)
    {
        return value.Split(',').Select(v => v.Trim()).ToList();
    }
}