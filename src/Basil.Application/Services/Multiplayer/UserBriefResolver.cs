using Basil.Application.Abstractions.Users;
using Basil.Application.Sessions;
using Basil.Domain.Login;

namespace Basil.Application.Services.Multiplayer;

/// <summary>
///     Resolves a <see cref="UserBrief" /> for any user id — online players are resolved instantly
///     from <see cref="IPlayerSessionRegistry" />, offline ones fall back to the (cached)
///     <see cref="IUserRepository" />. Null only when neither source knows the id.
/// </summary>
public static class UserBriefResolver
{
    public static async Task<UserBrief?> ResolveAsync(int userId, IPlayerSessionRegistry sessionRegistry,
        IUserRepository users, CancellationToken cancellationToken = default)
    {
        var session = sessionRegistry.GetById(userId);
        if (session is not null) return new UserBrief(session.Id, session.Name, session.Geoloc.Country.ToAcronym());

        var user = await users.FetchByIdAsync(userId, cancellationToken);
        return user is null ? null : new UserBrief(user.Id, user.Name, user.Country.ToAcronym());
    }
}
