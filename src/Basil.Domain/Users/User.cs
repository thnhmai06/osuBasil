using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using Basil.Domain.Login;

namespace Basil.Domain.Users;

/// <summary>
///     Ported from app/repositories/users.py's User dataclass, scoped to fields Basil actually reads
///     back somewhere — bancho.py's clan/preferred-mode/play-style/custom-badge/userpage columns
///     have no reader anywhere in this server (clans, public profiles, and a general-purpose v1/v2
///     API are out of scope — see docs/working-scopes.md) and aren't carried on this record.
/// </summary>
public sealed partial record User(
    int Id,
    string Name,
    Country Country,
    UserPrivileges Priv,
    DateTimeOffset SilenceEnd)
{
    /// <summary>
    ///     Normalises a username for case/space-insensitive identity — matching real osu!'s own
    ///     dedup rule ("Peppy" == "peppy" == "pe_ppy" == "pe ppy"). A DB-lookup/uniqueness detail,
    ///     not carried as a field on <see cref="User" /> itself.
    /// </summary>
    public static string MakeSafeName(string name)
    {
        return name.ToLowerInvariant().Replace(' ', '_');
    }

    /// <summary>
    ///     Validates a username against osu!'s real registration rules. Returns true when valid;
    ///     otherwise false with <paramref name="error" /> set to a user-facing message.
    /// </summary>
    public static bool ValidateUsername(string name, [MaybeNullWhen(true)] out string error)
    {
        if (name.Length is < 3 or > 15) error = "Username must be between 3 and 15 characters.";
        else if (name.StartsWith(' ') || name.EndsWith(' ')) error = "Username cannot start or end with a space.";
        else if (name.Contains("  ")) error = "Username cannot contain consecutive spaces.";
        else if (!AllowedUsernameCharacters.IsMatch(name))
            error = "Username may only contain letters, numbers, spaces, and _ - [ ].";
        else error = null;

        return error is null;
    }
    
    private static readonly Regex AllowedUsernameCharacters = OsuUsernamePattern();
    
    [GeneratedRegex(@"^[a-zA-Z0-9_\-\[\] ]+$")]
    private static partial Regex OsuUsernamePattern();
}
