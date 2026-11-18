using Basil.Application.Anticheat.Objects.Events;
using Basil.Application.Anticheat.Services;
using Basil.Application.Shared.Events;
using Basil.Application.Users.Objects.Sessions;
using Basil.Domain.Game.Client;

namespace Basil.Infrastructure.Anticheat.Services;

internal sealed class AnticheatReporter(IEventPublisher<AnticheatEvent> events) : IAnticheatReporter
{
	public OsuFlags Report(StableSession player, OsuFlags flags)
	{
		var signs = flags & OsuFlagsGroups.CheatSigns;
		if (signs == OsuFlags.Clean) return signs;

		events.Publish(new AnticheatPlayerFlagged(player, signs));
		return signs;
	}
}