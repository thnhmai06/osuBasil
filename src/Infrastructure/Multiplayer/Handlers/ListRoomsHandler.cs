using Basil.Application.Multiplayer.Messages;
using Basil.Application.Multiplayer.Objects.Rooms;
using Basil.Application.Multiplayer.Registry;
using Basil.Application.Shared.Paging;
using Mediator;

namespace Basil.Infrastructure.Multiplayer.Handlers;

/// <summary>Lists the open rooms the connection that asked may view and a query includes.</summary>
internal sealed class ListRoomsHandler(Lobby lobby) : IRequestHandler<ListRooms, Page<Room>>
{
	/// <inheritdoc />
	public ValueTask<Page<Room>> Handle(ListRooms request, CancellationToken ct)
	{
		return ValueTask.FromResult(lobby.ScopeTo(request.Actor).SearchRooms(request.Query, request.Page));
	}
}
