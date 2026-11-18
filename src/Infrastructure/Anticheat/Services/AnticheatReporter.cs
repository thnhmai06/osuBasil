using Basil.Application.Anticheat.Objects.Events;
using Basil.Application.Anticheat.Services;
using Basil.Application.Shared.Events;
using Basil.Application.Users.Objects.Connections;
using Basil.Domain.Client;

namespace Basil.Infrastructure.Anticheat.Services;

internal sealed class AnticheatReporter(IEventPublisher<AnticheatEvent> events) : IAnticheatReporter
{
	public OsuFlags Report(StableConnection player, OsuFlags flags)
	{
		var signs = flags & OsuFlagsGroups.CheatSigns;
		if (signs == OsuFlags.Clean) return signs;

		events.Publish(new AnticheatPlayerFlagged(player, signs));
		return signs;
	}
}