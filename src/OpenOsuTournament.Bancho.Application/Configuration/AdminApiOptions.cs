namespace OpenOsuTournament.Bancho.Application.Configuration;

/// <summary>
///     New for the api. host's management REST endpoints (beatmap/user/replay/match/seasonal CRUD) —
///     bancho.py has no equivalent admin surface. AdminKey gates every management route via the
///     X-Admin-Key request header; unset (the default) locks every management route down rather than
///     leaving them open.
/// </summary>
public sealed class AdminApiOptions
{
    public const string SectionName = "Api";

    public string? AdminKey { get; init; }
}
