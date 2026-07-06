namespace Bancho.Application.Configuration;

/// <summary>
/// Ports DOMAIN, COMMAND_PREFIX, SEASONAL_BGS, MENU_ICON_URL, MENU_ONCLICK_URL, DEBUG,
/// REDIRECT_OSU_URLS, DEVELOPER_MODE, LOG_WITH_COLORS, AUTOMATICALLY_REPORT_PROBLEMS
/// from app/settings.py.
/// </summary>
public sealed class ServerBehaviorOptions
{
    public const string SectionName = "ServerBehavior";

    public required string Domain { get; init; }
    public required string CommandPrefix { get; init; }
    public IReadOnlyList<string> SeasonalBackgrounds { get; init; } = [];
    public required string MenuIconUrl { get; init; }
    public required string MenuOnclickUrl { get; init; }
    public bool Debug { get; init; }
    public bool RedirectOsuUrls { get; init; }
    public bool DeveloperMode { get; init; }
    public bool LogWithColors { get; init; }
    public bool AutomaticallyReportProblems { get; init; }
}
