using Basil.Application.Anticheat.Objects.Events;
using Basil.Application.Anticheat.Services;
using Basil.Application.Shared.Events;
using Basil.Application.Sessions.Objects;
using Basil.Domain.Client;

namespace Basil.Infrastructure.Anticheat.Services;

internal sealed class AnticheatReporter(IEventPublisher<AnticheatEvent> events) : IAnticheatReporter
{
	public ClientFlags Report(StableSession player, ClientFlags flags)
	{
		var signs = flags & ClientFlagsGroups.CheatSigns;
		if (signs == ClientFlags.Clean) return signs;

		events.Publish(new AnticheatPlayerFlagged(player, signs));
		return signs;
	}
}