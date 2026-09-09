using Basil.Server.Features.Multiplayer.Endpoints;

namespace Basil.Server.Features.Multiplayer;

/// <summary>
///     Registers the REST endpoints for listing, creating, and streaming multiplayer matches.
/// </summary>
/// <remarks>
///     Match listings, settings, and live streams are publicly readable, while creating a match and
///     modifying its settings require administrator authorization. Live streams are dedicated
///     server-sent-events channels that only open for matches that are currently live.
/// </remarks>
internal static class MatchRoutes
{
	/// <summary>
	///     Registers the `/matches` list, create, report, settings, and live-stream routes, plus every
	///     `/matches/{matchId}` sub-resource, on the `api.` host.
	/// </summary>
	/// <param name="group">The `api.` host route group.</param>
	public static void MapMatchRoutes(this RouteGroupBuilder group)
	{
		group.MapMatchList();
		group.MapMatchReport();
		group.MapMatchSettings();
		group.MapMatchLiveStreams();
		group.MapMatchSubResourceRoutes();
	}
}