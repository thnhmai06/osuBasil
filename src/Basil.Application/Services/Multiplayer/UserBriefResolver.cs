using Basil.Application.Abstractions.Users;
using Basil.Application.Sessions;

namespace Basil.Application.Services.Multiplayer;

/// <summary>
///     Resolves a <see cref="UserBrief" /> for a user id.
/// </summary>
/// <remarks>
///     Online players are resolved instantly from <see cref="IPlayerSessionRegistry" />; offline
///     ones fall back to the user repository. The result is <see langword="null" /> only when
///     neither source knows the id.
/// </remarks>
public static class UserBriefResolver
{
	/// <summary>Resolves a user id to a brief from the session registry or the user repository.</summary>
	/// <param name="userId">The user id to resolve.</param>
	/// <param name="sessionRegistry">The registry used to resolve online players.</param>
	/// <param name="users">The repository used to resolve offline players.</param>
	/// <param name="cancellationToken">A token that cancels the offline lookup.</param>
	/// <returns>The <see cref="UserBrief" /> for the id, or <see langword="null" /> when neither source knows it.</returns>
	public static async Task<UserBrief?> ResolveAsync(int userId, IPlayerSessionRegistry sessionRegistry,
		IUserRepository users, CancellationToken cancellationToken = default)
	{
		var session = sessionRegistry.GetById(userId);
		if (session is not null) return new UserBrief(session.Id, session.Name, session.Geoloc.Country);

		var user = await users.FetchByIdAsync(userId, cancellationToken);
		return user is null ? null : new UserBrief(user.Id, user.Name, user.Country);
	}
}