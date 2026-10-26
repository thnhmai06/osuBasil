using Basil.Application.Runtime.Events;
using Basil.Application.Runtime.Sessions;
using Basil.Domain.Bancho;

namespace Basil.Application.Runtime.Anticheat;

/// <summary>Reports the anticheat flags a game client sent and announces the signs of cheating among them.</summary>
public sealed class AnticheatReporter
{
	private readonly IEventPublisher<AnticheatEvent> _events;

	/// <summary>Creates the reporter with the publisher of anticheat events.</summary>
	/// <param name="events">The publisher for anticheat events.</param>
	internal AnticheatReporter(IEventPublisher<AnticheatEvent> events)
	{
		_events = events;
	}

	/// <summary>Reports the anticheat flags a game client sent.</summary>
	/// <param name="player">The player's game client.</param>
	/// <param name="flags">The flags the client reported.</param>
	/// <returns>The reported flags that are signs of cheating; <see cref="BanchoFlags.Clean" /> when there are none.</returns>
	/// <remarks>
	///     This system operation needs no rights because the server itself calls it.
	///     Nothing is blocked. When the flags show signs of cheating, one <see cref="AnticheatPlayerFlagged" /> carries
	///     them and the player; whoever warns the room's chat, referees and creator finds the room itself.
	/// </remarks>
	public BanchoFlags Report(BanchoConnection player, BanchoFlags flags)
	{
		var signs = flags & BanchoFlagsGroups.CheatSigns;
		if (signs == BanchoFlags.Clean) return signs;

		_events.Publish(new AnticheatPlayerFlagged(player, signs));
		return signs;
	}
}
