using Basil.Application.Multiplayer.Objects;
using Basil.Application.Multiplayer.Messages;
using Mediator;

namespace Basil.Infrastructure.Multiplayer.Handlers;

internal sealed class AddRefereeHandler : IRequestHandler<AddReferee, LobbyResult>
{
	public ValueTask<LobbyResult> Handle(AddReferee request, CancellationToken ct)
	{
		return new ValueTask<LobbyResult>(
			request.Room.ScopeTo(request.Actor).Authority.AddRefereeAsync(request.User));
	}
}