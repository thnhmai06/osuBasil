namespace Basil.Host.Api.Beatmaps;

/// <summary>Maps the Beatmaps slice's routes.</summary>
public static class BeatmapsRouteMapping
{
	/// <summary>Maps the Beatmaps slice's routes onto the `api.` host.</summary>
	/// <param name="group">The `api.{domain}` route group.</param>
	public static void MapBeatmapsRoutes(this RouteGroupBuilder group)
	{
		group.MapBeatmapsetRoutes();
	}
}
