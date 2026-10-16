namespace Basil.Host.Api.Multiplayer;

/// <summary>Maps the Multiplayer slice's routes.</summary>
public static class MultiplayerRouteMapping
{
	/// <summary>Maps the Multiplayer slice's routes onto the `api.` host.</summary>
	/// <param name="group">The `api.{domain}` route group.</param>
	public static void MapMultiplayerRoutes(this RouteGroupBuilder group)
	{
		group.MapMatchRoutes();
	}
}