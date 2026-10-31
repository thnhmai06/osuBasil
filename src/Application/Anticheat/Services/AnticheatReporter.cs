using Basil.Application.Anticheat.Objects.Events;
using Basil.Application.Common.Events;
using Basil.Application.Users.Objects.Connections;
using Basil.Domain.Client;

namespace Basil.Application.Anticheat.Services;

/// <summary>Reports the anticheat flags a game client sent and announces the signs of cheating among them.</summary>
internal sealed class AnticheatReporter : IAnticheatReporter
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
	/// <returns>The reported flags that are signs of cheating; <see cref="OsuFlags.Clean" /> when there are none.</returns>
	/// <remarks>One <see cref="AnticheatPlayerFlagged" /> carries the signs and the player when there are any.</remarks>
	public OsuFlags Report(BanchoConnection player, OsuFlags flags)
	{
		var signs = flags & OsuFlagsGroups.CheatSigns;
		if (signs == OsuFlags.Clean) return signs;

		_events.Publish(new AnticheatPlayerFlagged(player, signs));
		return signs;
	}
}