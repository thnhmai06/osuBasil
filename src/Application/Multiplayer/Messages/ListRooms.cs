using Basil.Application.Multiplayer.Objects.Rooms;
using Basil.Application.Multiplayer.Registry;
using Basil.Application.Shared.Paging;
using Basil.Application.Users.Objects.Connections;
using Mediator;

namespace Basil.Application.Multiplayer.Messages;

public sealed record ListRooms(Connection Actor, RoomQuery Query, PageRequest Page) : IRequest<Page<Room>>;
