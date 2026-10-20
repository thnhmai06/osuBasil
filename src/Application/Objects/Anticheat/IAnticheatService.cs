using Basil.Application.Objects.Events;
using Basil.Application.Objects.Sessions;
using Basil.Domain.Bancho;

namespace Basil.Application.Objects.Anticheat;

/// <summary>Judges the anticheat flags osu! clients report and announces signs of cheating.</summary>
public interface IAnticheatService : IEventPublisher<AnticheatEvent>
{
	/// <summary>Reports the anticheat flags a player's osu! client sent.</summary>
	/// <param name="player">The player's osu! client.</param>
	/// <param name="flags">The flags the client reported.</param>
	/// <returns>The reported flags that are signs of cheating; <see cref="BanchoFlags.Clean" /> when there are none.</returns>
	/// <remarks>
	///     Nothing is blocked. When the flags show signs of cheating, one <see cref="AnticheatPlayerFlagged" /> carries
	///     them and the room the player sits in, so the room's chat, referees and creator can be warned.
	/// </remarks>
	BanchoFlags Report(BanchoConnection player, BanchoFlags flags);
}
