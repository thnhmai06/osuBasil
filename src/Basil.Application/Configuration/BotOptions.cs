namespace Basil.Application.Configuration;

/// <summary>
///     Re-adding BanchoBot (see docs/scope-decisions.md for why it was deleted, then re-approved for
///     this tourney-command pivot). Name is customizable so an unrestricted rename doesn't collide
///     with whatever the seed migration wrote for the id=0 row.
/// </summary>
public sealed class BotOptions
{
    public const string SectionName = "Bot";

    public string Name { get; init; } = "BasilBot";

    /// <summary>Prefix chat commands must start with (!help, !roll, !mp ...).</summary>
    public required string CommandPrefix { get; init; }

    /// <summary>Country code for BasilBot (default 'vn'). Seeded in base.sql, overridable here.</summary>
    public string Country { get; init; } = "vn";
}