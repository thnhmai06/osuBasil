using Basil.Application.Multiplayer.Messages;
using Basil.Domain.Multiplayer.Room;
using Mediator;

namespace Basil.Infrastructure.Multiplayer.Handlers;

internal sealed class StartRoundHandler(ISender sender) : IRequestHandler<StartRound, RoomActionResult>
{
	public ValueTask<RoomActionResult> Handle(StartRound request, CancellationToken ct)
	{
		if (request.Countdown is { } countdown && countdown > TimeSpan.Zero)
			return sender.Send(new StartRoomCountdown(request.Actor, request.Room, countdown, true), ct);

		return new ValueTask<RoomActionResult>(
			request.Room.ScopeTo(request.Actor).Rounds.StartAsync());
	}
}