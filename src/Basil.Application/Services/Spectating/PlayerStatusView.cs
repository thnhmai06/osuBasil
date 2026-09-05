using Basil.Application.Sessions;
using Basil.Domain.Beatmaps;
using Basil.Domain.Scores;
using Basil.Domain.Users;

namespace Basil.Application.Services.Spectating;

/// <summary>
///     The wire shape of a userSession's live status, published on the <c>GET /users/{idOrName}/live</c>
///     stream's <c>status</c> event.
/// </summary>
/// <param name="Online">Whether the userSession currently has an active game session.</param>
/// <param name="Activity">The activity the userSession is currently reporting, or <see langword="null" /> when offline.</param>
/// <param name="InfoText">The free-form status text accompanying the activity, or <see langword="null" /> when offline.</param>
/// <param name="MapId">
///     The id of the beatmap the userSession is currently playing or selecting, or <see langword="null" />
///     when offline or no beatmap is selected.
/// </param>
/// <param name="Mods">The mods the userSession currently has active, or <see langword="null" /> when offline.</param>
/// <param name="Mode">The game mode the userSession currently has selected, or <see langword="null" /> when offline.</param>
public sealed record PlayerStatusView(
	bool Online,
	UserActivity? Activity,
	string? InfoText,
	int? MapId,
	Mods? Mods,
	GameMode? Mode)
{
	/// <summary>Builds the current status view for a userSession.</summary>
	/// <param name="session">
	///     The userSession's live <see cref="GameSession" />, or <see langword="null" /> when the
	///     userSession is not currently online.
	/// </param>
	/// <returns>The <see cref="PlayerStatusView" /> reflecting <paramref name="session" />'s current status.</returns>
	public static PlayerStatusView Build(GameSession? session)
	{
		if (session is null) return new PlayerStatusView(false, null, null, null, null, null);

		var status = session.Status;
		return new PlayerStatusView(true, status.UserActivity, status.InfoText,
			status.MapId > 0 ? status.MapId : null, status.Mods, status.Mode);
	}
}
