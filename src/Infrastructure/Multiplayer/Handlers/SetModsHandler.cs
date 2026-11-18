using Basil.Application.Multiplayer.Messages;
using Basil.Application.Multiplayer.Objects;
using Mediator;

namespace Basil.Infrastructure.Multiplayer.Handlers;

internal sealed class SetModsHandler : IRequestHandler<SetMods, RoomOutcome>
{
	public ValueTask<RoomOutcome> Handle(SetMods request, CancellationToken ct)
	{
		return new ValueTask<RoomOutcome>(
			request.Room.ScopeTo(request.Actor).Slots.SetModsAsync(request.Mods));
	}
}