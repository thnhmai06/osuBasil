using Basil.Application.Multiplayer.Messages;
using Basil.Infrastructure.Bot.Workers;
using Mediator;

namespace Basil.Infrastructure.Bot.Handlers;

internal sealed class HasRoomCountdownHandler(RoomCountdowns countdowns) : IRequestHandler<HasRoomCountdown, bool>
{
	public ValueTask<bool> Handle(HasRoomCountdown request, CancellationToken ct)
	{
		return ValueTask.FromResult(countdowns.IsRunning(request.Room));
	}
}