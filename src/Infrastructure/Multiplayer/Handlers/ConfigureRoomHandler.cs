using Basil.Application.Multiplayer.Messages;
using Basil.Application.Multiplayer.Objects;
using Basil.Application.Shared;
using Mediator;

namespace Basil.Infrastructure.Multiplayer.Handlers;

/// <summary>Runs a <see cref="ConfigureRoom" /> on the room for the connection that asked.</summary>
internal sealed class ConfigureRoomHandler : IRequestHandler<ConfigureRoom, RoomOutcome>
{
	/// <inheritdoc />
	public ValueTask<RoomOutcome> Handle(ConfigureRoom request, CancellationToken ct)
	{
		return new ValueTask<RoomOutcome>(
			request.Room.ScopeTo(request.Actor).Settings.ConfigureAsync(request.Change));
	}
}