using Basil.Application.Abstractions.Users;

namespace Basil.Web.Routing;

/// <summary>
///     Shared "accept a username in place of a numeric userId" helper for public GET routes under
///     `/users`. A numeric segment is served directly; a non-numeric one is resolved via
///     <see cref="IUserRepository.FetchByNameAsync" /> and 302-redirected to the canonical
///     `{canonicalPath}`; write routes (PUT/PATCH/DELETE) never use this, since a redirect doesn't
///     reliably carry a request body across most HTTP clients/proxies.
/// </summary>
internal static class UserLookup
{
	/// <summary>
	///     Serves a numeric <paramref name="idOrName" /> directly via <paramref name="onId" />. A
	///     non-numeric value is resolved via <see cref="IUserRepository.FetchByNameAsync" /> and
	///     302-redirected to <paramref name="canonicalPath" />, or returns 404 when the name resolves
	///     to no user.
	/// </summary>
	/// <param name="idOrName">The path segment: a numeric user id or a username.</param>
	/// <param name="users">The user repository used for the by-name lookup.</param>
	/// <param name="canonicalPath">Builds the canonical `{userId}` path a resolved name is redirected to.</param>
	/// <param name="onId">Serves the request for a directly-given numeric user id.</param>
	/// <param name="cancellationToken">A token that cancels the by-name lookup.</param>
	/// <returns>A result that serves the numeric id, redirects the resolved name, or reports 404.</returns>
	public static async Task<IResult> ResolveAsync(string idOrName, IUserRepository users,
		Func<int, string> canonicalPath, Func<int, Task<IResult>> onId, CancellationToken cancellationToken)
	{
		if (int.TryParse(idOrName, out var id)) return await onId(id);

		var user = await users.FetchByNameAsync(idOrName, cancellationToken);
		return user is null ? Results.NotFound() : Results.Redirect(canonicalPath(user.Id));
	}
}