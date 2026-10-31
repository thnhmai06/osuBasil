using Basil.Application.Users.Objects.Connections;
using Basil.Domain.Client;

namespace Basil.Application.Anticheat.Services;

/// <summary>Reports the anticheat flags a game client sent.</summary>
public interface IAnticheatReporter
{
	/// <summary>Reports the anticheat flags a game client sent.</summary>
	/// <param name="player">The player's game client.</param>
	/// <param name="flags">The flags the client reported.</param>
	/// <returns>The reported flags that are signs of cheating; <see cref="OsuFlags.Clean" /> when there are none.</returns>
	/// <remarks>
	///     The server itself calls this system operation, so it needs no rights. Nothing is blocked: when the flags show
	///     signs of cheating the server announces them, and whoever warns the room's chat, the referees and the creator
	///     finds the room itself.
	/// </remarks>
	OsuFlags Report(BanchoConnection player, OsuFlags flags);
}