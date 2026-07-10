namespace Basil.Application.Configuration;

/// <summary>Ports DOMAIN, MENU_ICON_URL, MENU_ONCLICK_URL from app/settings.py.</summary>
public sealed class ServerOptions
{
    public const string SectionName = "Server";

    public required string Domain { get; init; }

    /// <summary>Kestrel HTTPS listen port. Disables automatic port selection.</summary>
    public int Port { get; init; } = 443;

    /// <summary>Path to the HTTPS certificate file (PFX).</summary>
    public string? CertPath { get; init; }

    /// <summary>Password for the HTTPS certificate.</summary>
    public string? CertPassword { get; init; }

    /// <summary>
    ///     Local file path to the in-game menu icon image, served back by `GET /web/menu-icon` on the
    ///     `osu.` host (see BanchoHostGroups) rather than sent to the client as-is — the client just
    ///     gets that endpoint's URL. Relative paths resolve against the executable's directory.
    /// </summary>
    public required string MenuIconPath { get; init; }

    public required string MenuOnclickUrl { get; init; }

    /// <summary>
    ///     Gates every api.&lt;domain&gt; management REST route (beatmap/user/replay/match/seasonal CRUD)
    ///     via the X-Admin-Key header. Also used by the in-game registration endpoint (osu. POST /users)
    ///     as the secret the client must send in the Email field to self-register.
    ///     Leave unset to lock management routes down (401) and disable in-game registration.
    /// </summary>
    public string? AdminKey { get; init; }
}
