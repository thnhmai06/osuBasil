namespace Basil.Host.Api.Scores;

/// <summary>Maps the Scores slice's routes.</summary>
public static class ScoresRouteMapping
{
	/// <summary>Maps the Scores slice's routes onto the `api.` host.</summary>
	/// <param name="group">The `api.{domain}` route group.</param>
	public static void MapScoresRoutes(this RouteGroupBuilder group)
	{
		group.MapScoreRoutes();
	}
}