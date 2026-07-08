namespace Basil.Domain.Users;

/// <summary>Ported from app/utils.py's make_safe_name — normalizes a username for lookup/uniqueness.</summary>
public static class SafeName
{
    public static string Make(string name)
    {
        return name.ToLowerInvariant().Replace(' ', '_');
    }
}