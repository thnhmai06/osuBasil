namespace Basil.Host.Api.Users;

/// <summary>Maps the Users slice's routes.</summary>
public static class UsersRouteMapping
{
	/// <summary>Maps the Users slice's routes onto the `api.` host.</summary>
	/// <param name="group">The `api.{domain}` route group.</param>
	public static void MapUsersRoutes(this RouteGroupBuilder group)
	{
		group.MapUserRoutes();
	}
}
