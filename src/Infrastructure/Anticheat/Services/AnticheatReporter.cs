using Basil.Application.Anticheat.Objects.Events;
using Basil.Application.Anticheat.Services;
using Basil.Application.Common.Events;
using Basil.Application.Users.Objects.Connections;
using Basil.Domain.Client;

namespace Basil.Infrastructure.Anticheat.Services;

/// <summary>Reports the anticheat flags a game client sent and announces the signs of cheating among them.</summary>
internal sealed class AnticheatReporter(IEventPublisher<AnticheatEvent> events) : IAnticheatReporter
{
	/// <inheritdoc />
	public OsuFlags Report(StableConnection player, OsuFlags flags)
	{
		var signs = flags & OsuFlagsGroups.CheatSigns;
		if (signs == OsuFlags.Clean) return signs;

		events.Publish(new AnticheatPlayerFlagged(player, signs));
		return signs;
	}
}