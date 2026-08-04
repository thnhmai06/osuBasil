using Basil.Application.Abstractions.Users;

// ReSharper disable ClassNeverInstantiated.Global
// ReSharper disable MemberCanBePrivate.Global
// ReSharper disable NotAccessedPositionalProperty.Global

namespace Basil.Web.Routing.Api;

/// <summary>
///     Resolves a `/users` route parameter that may be either a numeric user id or a username.
/// </summary>
/// <remarks>
///     Numeric ids are served directly. Usernames are resolved and redirected to the canonical
///     user-id URL. Returns 404 if the username does not exist.
/// </remarks>
internal static class UserLookup
{
	/// <summary>
	///     Resolves a user id or username to a canonical user-id route.
	/// </summary>
	/// <param name="idOrName">A numeric user id or username.</param>
	/// <param name="users">The user repository.</param>
	/// <param name="canonicalPath">Builds the canonical URL for a resolved user id.</param>
	/// <param name="onId">Handles requests that already specify a numeric user id.</param>
	/// <param name="cancellationToken">A token that cancels the lookup.</param>
	/// <returns>
	///     The result produced by <paramref name="onId" />, a redirect to the canonical user-id URL,
	///     or <see cref="Results.NotFound" /> if the username does not exist.
	/// </returns>
	public static async Task<IResult> ResolveAsync(string idOrName, IUserRepository users,
		Func<int, string> canonicalPath, Func<int, Task<IResult>> onId, CancellationToken cancellationToken)
	{
		if (int.TryParse(idOrName, out var id)) return await onId(id);

		var user = await users.FetchByNameAsync(idOrName, cancellationToken);
		return user is null ? Results.NotFound() : Results.Redirect(canonicalPath(user.Id));
	}
}