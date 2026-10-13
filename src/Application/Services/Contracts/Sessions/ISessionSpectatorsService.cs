using Basil.Application.Storage.Contracts.Sessions;
using Basil.Domain.Users;

namespace Basil.Application.Services.Contracts.Sessions;

/// <summary>Starts and stops spectating osu! clients.</summary>
public interface ISessionSpectatorsService
{
	/// <summary>Starts spectating an osu! client.</summary>
	/// <param name="host">The client being spectated.</param>
	/// <param name="by">The connection that starts spectating.</param>
	/// <returns>The outcome.</returns>
	/// <remarks>
	///     Needs <see cref="Permissions.PlayerSpectate" />, which <see cref="Permissions.TournamentObserveRooms" />
	///     includes; NotPermitted otherwise. The spectator joins the host's spectator channel, and the first spectator
	///     brings the host into it too.
	/// </remarks>
	/// <exception cref="ArgumentException"><paramref name="by" /> is an IRC connection, which cannot spectate.</exception>
	SpectateResult Spectate(BanchoConnection host, Connection by);

	/// <summary>Stops spectating.</summary>
	/// <param name="by">The spectator that stops.</param>
	/// <returns><see langword="true" /> if the connection was spectating.</returns>
	/// <remarks>
	///     The spectator leaves the host's spectator channel, and the host leaves it too when no spectator is left.
	///     Calling it again does nothing.
	/// </remarks>
	bool StopSpectating(Connection by);

	/// <summary>Reports that a spectator failed to spectate its host, for example because it lacks the beatmap.</summary>
	/// <param name="by">The spectator reporting it.</param>
	/// <returns><see langword="true" /> if the connection was spectating.</returns>
	bool ReportSpectatingFailed(Connection by);
}

/// <summary>The outcome of starting to spectate a player.</summary>
public enum SpectateResult : byte
{
	/// <summary>The connection is now spectating the host.</summary>
	Spectating,

	/// <summary>The host's connection is closed.</summary>
	TargetOffline,

	/// <summary>A user cannot spectate themselves.</summary>
	Self,

	/// <summary>The connection is already spectating this host.</summary>
	AlreadySpectating,

	/// <summary>The spectator is not granted the permission to spectate, or it is suspended.</summary>
	NotPermitted
}
