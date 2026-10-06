using Basil.Domain.Users;
using Basil.Application.Storage.Contracts.Sessions;

namespace Basil.Application.Services.Contracts.Chat;

/// <summary>Starts and stops spectating osu! players.</summary>
public interface IChannelSpectatorService
{
	/// <summary>Starts spectating an osu! client.</summary>
	/// <param name="host">The client being spectated.</param>
	/// <param name="by">The connection that starts spectating.</param>
	/// <returns>The outcome.</returns>
	/// <remarks>
	///     osu! clients and HTTP API clients can spectate with <see cref="Permissions.PlayerSpectate" />, osu!tourney
	///     clients with <see cref="Permissions.TournamentObserveRooms" />; NotPermitted otherwise.
	/// </remarks>
	/// <exception cref="ArgumentException"><paramref name="by" /> is an IRC connection.</exception>
	SpectateResult Spectate(BanchoConnection host, Connection by);

	/// <summary>Stops spectating.</summary>
	/// <param name="by">The spectator that stops.</param>
	/// <returns><see langword="true" /> if the connection was spectating.</returns>
	bool StopSpectating(Connection by);

	/// <summary>Reports that a spectator failed to spectate its host, for example because it lacks the beatmap.</summary>
	/// <param name="by">The spectator reporting it.</param>
	/// <returns><see langword="true" /> if the connection was spectating.</returns>
	bool ReportSpectatingFailed(Connection by);
}
