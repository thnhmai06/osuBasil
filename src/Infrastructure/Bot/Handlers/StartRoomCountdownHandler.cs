using Basil.Application.Bot.Registry;
using Basil.Application.Common;
using Basil.Application.Multiplayer.Messages;
using Basil.Application.Multiplayer.Objects;
using Mediator;

namespace Basil.Infrastructure.Bot.Handlers;

/// <summary>Starts the countdown of a <see cref="StartRoomCountdown" /> when the connection may start rounds.</summary>
internal sealed class StartRoomCountdownHandler(ISender sender, RoomCountdowns countdowns)
	: IRequestHandler<StartRoomCountdown, Result<RoomOutcome>>
{
	/// <inheritdoc />
	public async ValueTask<Result<RoomOutcome>> Handle(StartRoomCountdown request, CancellationToken cancellationToken)
	{
		var allowed = await sender.Send(new CheckRoundStart(request.Actor, request.Room), cancellationToken);
		return allowed.IsOk
			? await countdowns.StartAsync(request.Actor, request.Room, request.Duration, request.StartsRound,
				cancellationToken)
			: allowed;
	}
}