using Basil.Application.Multiplayer.Messages;
using Basil.Application.Multiplayer.Objects;
using Basil.Application.Shared;
using Basil.Infrastructure.Bot.Workers;
using Mediator;

namespace Basil.Infrastructure.Bot.Handlers;

/// <summary>Starts the countdown of a <see cref="StartRoomCountdown" /> when the connection may start rounds.</summary>
internal sealed class StartRoomCountdownHandler(ISender sender, RoomCountdowns countdowns)
	: IRequestHandler<StartRoomCountdown, RoomOutcome>
{
	/// <inheritdoc />
	public async ValueTask<RoomOutcome> Handle(StartRoomCountdown request, CancellationToken ct)
	{
		var allowed = await sender.Send(new CheckRoundStart(request.Actor, request.Room), ct);
		return allowed == RoomOutcome.Ok
			? await countdowns.StartAsync(request.Actor, request.Room, request.Duration, request.StartsRound,
				ct)
			: allowed;
	}
}