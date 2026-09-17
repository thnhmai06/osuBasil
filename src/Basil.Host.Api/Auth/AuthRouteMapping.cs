namespace Basil.Host.Api.Auth;

/// <summary>Maps the Auth slice's routes.</summary>
public static class AuthRouteMapping
{
	/// <summary>Maps the Auth slice's routes onto the `api.` host's `/settings` group.</summary>
	/// <param name="group">The `api.{domain}` route group.</param>
	public static void MapAuthRoutes(this RouteGroupBuilder group)
	{
		group.MapGroup("/settings").MapAdminKeyRoutes();
	}
}
