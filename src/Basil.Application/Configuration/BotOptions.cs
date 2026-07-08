namespace Basil.Application.Configuration;

/// <summary>
///     Re-adding BanchoBot (see docs/scope-decisions.md for why it was deleted, then re-approved for
///     this tourney-command pivot). Name is customizable so an unrestricted rename doesn't collide
///     with whatever the seed migration wrote for the id=1 row.
/// </summary>
public sealed class BotOptions
{
    public const string SectionName = "Bot";

    public string Name { get; init; } = "BasilBot";
}