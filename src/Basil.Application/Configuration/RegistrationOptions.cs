namespace Basil.Application.Configuration;

/// <summary>
///     Ports DISALLOWED_NAMES, DISALLOWED_PASSWORDS, DISALLOW_INGAME_REGISTRATION from
///     app/settings.py. DISALLOW_OLD_CLIENTS is not ported — the check it gated
///     (IOsuVersionAllowlistProvider) queried osu!api's changelog over the network, and this server
///     runs fully offline, so every client version is allowed through.
/// </summary>
public sealed class RegistrationOptions
{
    public const string SectionName = "Registration";

    public IReadOnlyList<string> DisallowedNames { get; init; } = [];
    public IReadOnlyList<string> DisallowedPasswords { get; init; } = [];
    public bool DisallowIngameRegistration { get; init; }
}