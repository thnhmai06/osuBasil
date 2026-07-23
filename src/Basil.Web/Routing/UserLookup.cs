using Basil.Application.Abstractions.Users;

namespace Basil.Web.Routing;

/// <summary>
///     Shared "accept a username in place of a numeric userId" helper for public GET routes under
///     `/users`. A numeric segment is served directly; a non-numeric one is resolved via
///     <see cref="IUserRepository.FetchByNameAsync" /> and 302-redirected to the canonical
///     `{canonicalPath}` — write routes (PUT/PATCH/DELETE) never use this, since a redirect doesn't
///     reliably carry a request body across most HTTP clients/proxies.
/// </summary>
internal static class UserLookup
{
    public static async Task<IResult> ResolveAsync(string idOrName, IUserRepository users,
        Func<int, string> canonicalPath, Func<int, Task<IResult>> onId, CancellationToken cancellationToken)
    {
        if (int.TryParse(idOrName, out var id)) return await onId(id);

        var user = await users.FetchByNameAsync(idOrName, cancellationToken);
        return user is null ? Results.NotFound() : Results.Redirect(canonicalPath(user.Id));
    }
}
