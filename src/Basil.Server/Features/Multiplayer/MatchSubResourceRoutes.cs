using Basil.Server.Features.Multiplayer.Endpoints;

namespace Basil.Server.Features.Multiplayer;

/// <summary>
///     Registers the REST endpoints for a match's hosts, referees, bans, slots, timer, abort, and
///     close actions.
/// </summary>
/// <remarks>
///     Each resource is publicly readable as plain JSON (404 if the match isn't currently live) or as
///     a server-sent-events stream on its `/live` sibling (409 if the match isn't currently live).
///     Every write requires administrator authorization.
/// </remarks>
internal static class MatchSubResourceRoutes
{
	/// <summary>
	///     Registers the `/matches/{matchId}` host, referees, ban, slots, timer, abort, and close
	///     sub-routes on the `api.` host.
	/// </summary>
	/// <param name="group">The `api.` host route group.</param>
	public static void MapMatchSubResourceRoutes(this RouteGroupBuilder group)
	{
		group.MapMatchHosts();
		group.MapMatchReferees();
		group.MapMatchBans();
		group.MapMatchSlots();
		group.MapMatchTimer();
		group.MapMatchAbort();
		group.MapMatchClose();
		group.MapMatchChat();
	}
}