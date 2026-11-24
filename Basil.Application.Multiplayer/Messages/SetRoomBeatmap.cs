using Basil.Application.Sessions.Objects;
using Basil.Domain.Game;
using Basil.Domain.Multiplayer.Room;
using Mediator;
using Room = Basil.Application.Multiplayer.Objects.Rooms.Room;

namespace Basil.Application.Multiplayer.Messages;

public sealed record SetRoomBeatmap(Session Actor, Room Room, int BeatmapId, GameMode? Mode)
	: IRequest<RoomActionResult>;
