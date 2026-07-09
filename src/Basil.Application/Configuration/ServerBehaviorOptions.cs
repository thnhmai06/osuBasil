namespace Basil.Application.Configuration;

/// <summary>Ports DOMAIN, MENU_ICON_URL, MENU_ONCLICK_URL from app/settings.py.</summary>
public sealed class ServerBehaviorOptions
{
    public const string SectionName = "ServerBehavior";

    public required string Domain { get; init; }

    /// <summary>
    ///     Local file path to the in-game menu icon image, served back by `GET /web/menu-icon` on the
    ///     `osu.` host (see BanchoHostGroups) rather than sent to the client as-is — the client just
    ///     gets that endpoint's URL. Relative paths resolve against the executable's directory.
    /// </summary>
    public required string MenuIconPath { get; init; }

    public required string MenuOnclickUrl { get; init; }
}
