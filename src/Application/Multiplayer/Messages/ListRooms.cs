using Basil.Application.Multiplayer.Objects.Rooms;
using Basil.Application.Multiplayer.Registry;
using Basil.Application.Shared.Paging;
using Basil.Application.Users.Objects.Connections;
using Mediator;

namespace Basil.Application.Multiplayer.Messages;

/// <summary>Asks Multiplayer for a page of the open rooms a query includes.</summary>
/// <param name="Actor">The connection that asks.</param>
/// <param name="Query">Which rooms to include.</param>
/// <param name="Page">Which part of the listing to return.</param>
/// <remarks>Rooms are ordered by id. A private room is listed only when the actor may view it.</remarks>
public sealed record ListRooms(Connection Actor, RoomQuery Query, PageRequest Page) : IRequest<Page<Room>>;
