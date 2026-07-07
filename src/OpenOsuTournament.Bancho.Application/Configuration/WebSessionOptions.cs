namespace OpenOsuTournament.Bancho.Application.Configuration;

/// <summary>
///     Ports WEB_SESSION_COOKIE_SECURE from app/settings.py — should only be false for
///     plain-http local development.
/// </summary>
public sealed class WebSessionOptions
{
    public const string SectionName = "WebSession";

    public bool CookieSecure { get; init; } = true;
}