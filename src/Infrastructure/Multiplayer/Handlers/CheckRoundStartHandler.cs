using Basil.Application.Multiplayer.Messages;
using Basil.Application.Multiplayer.Objects;
using Mediator;

namespace Basil.Infrastructure.Multiplayer.Handlers;

/// <summary>Answers a <see cref="CheckRoundStart" /> from the privileges of the connection that asked.</summary>
internal sealed class CheckRoundStartHandler : IRequestHandler<CheckRoundStart, RoomOutcome>
{
	/// <inheritdoc />
	public ValueTask<RoomOutcome> Handle(CheckRoundStart request, CancellationToken ct)
	{
		return new ValueTask<RoomOutcome>(
			request.Room.ScopeTo(request.Actor).Rounds.RequireStart() ?? RoomOutcome.Ok);
	}
}