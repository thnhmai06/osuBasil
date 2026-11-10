using Basil.Application.Common;
using Basil.Application.Multiplayer.Messages;
using Basil.Application.Multiplayer.Objects;
using Mediator;

namespace Basil.Infrastructure.Multiplayer.Handlers;

/// <summary>Runs a <see cref="AddReferee" /> on the room for the connection that asked.</summary>
internal sealed class AddRefereeHandler : IRequestHandler<AddReferee, Result<RoomOutcome>>
{
	/// <inheritdoc />
	public ValueTask<Result<RoomOutcome>> Handle(AddReferee request, CancellationToken ct)
	{
		return new ValueTask<Result<RoomOutcome>>(
			request.Room.ScopeTo(request.Actor).Authority.AddRefereeAsync(request.User));
	}
}