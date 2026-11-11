using Basil.Application.Common;
using Basil.Application.Multiplayer.Messages;
using Basil.Application.Multiplayer.Objects;
using Mediator;

namespace Basil.Infrastructure.Multiplayer.Handlers;

/// <summary>Answers a <see cref="CheckRoundStart" /> from the privileges of the connection that asked.</summary>
internal sealed class CheckRoundStartHandler : IRequestHandler<CheckRoundStart, Result<RoomOutcome>>
{
	/// <inheritdoc />
	public ValueTask<Result<RoomOutcome>> Handle(CheckRoundStart request, CancellationToken ct)
	{
		return new ValueTask<Result<RoomOutcome>>(
			request.Room.ScopeTo(request.Actor).Rounds.RequireStart() ?? RoomOutcome.Ok);
	}
}