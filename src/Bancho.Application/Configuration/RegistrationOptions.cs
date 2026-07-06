namespace Bancho.Application.Configuration;

/// <summary>
/// Ports DISALLOWED_NAMES, DISALLOWED_PASSWORDS, DISALLOW_OLD_CLIENTS,
/// DISALLOW_INGAME_REGISTRATION from app/settings.py.
/// </summary>
public sealed class RegistrationOptions
{
    public const string SectionName = "Registration";

    public IReadOnlyList<string> DisallowedNames { get; init; } = [];
    public IReadOnlyList<string> DisallowedPasswords { get; init; } = [];
    public bool DisallowOldClients { get; init; }
    public bool DisallowIngameRegistration { get; init; }
}
