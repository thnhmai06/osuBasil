namespace Bancho.Domain;

/// <summary>Ported from app/utils.py's make_safe_name — normalizes a username for lookup/uniqueness.</summary>
public static class SafeName
{
    public static string Make(string name) => name.ToLowerInvariant().Replace(' ', '_');
}
