using Bancho.Application.Abstractions;
using Bancho.Application.Sessions;

namespace Bancho.Application.Commands;

/// <summary>
/// Ported from app/state/sessions.py's Players.from_cache_or_sql — looks up a target by name,
/// preferring the live online session (for its current name/casing) and falling back to the DB
/// for offline users. Shared by commands (block/unblock) that need to resolve an arbitrary target.
/// </summary>
internal static class CommandTargetResolver
{
    /// <summary>BanchoBot's fixed user id, per migrations/base.sql's seed insert.</summary>
    public const int BotId = 1;

    public static async Task<(int Id, string Name)?> ResolveAsync(IPlayerSessionRegistry sessionRegistry, IUserRepository users, string name)
    {
        var online = sessionRegistry.GetByName(name);
        if (online is not null)
        {
            return (online.Id, online.Name);
        }

        var user = await users.FetchByNameAsync(name);
        return user is null ? null : (user.Id, user.Name);
    }
}
