using Basil.Application.Multiplayer.Messages;
using Basil.Infrastructure.Bot.Workers;
using Mediator;

namespace Basil.Infrastructure.Bot.Handlers;

/// <summary>Tells whether a countdown runs in the room of a <see cref="HasRoomCountdown" />.</summary>
internal sealed class HasRoomCountdownHandler(RoomCountdowns countdowns) : IRequestHandler<HasRoomCountdown, bool>
{
	/// <inheritdoc />
	public ValueTask<bool> Handle(HasRoomCountdown request, CancellationToken ct)
	{
		return ValueTask.FromResult(countdowns.IsRunning(request.Room));
	}
}
