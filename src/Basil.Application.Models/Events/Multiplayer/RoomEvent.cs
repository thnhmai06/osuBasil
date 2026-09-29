using Basil.Application.Models.Multiplayer;
using Basil.Application.Models.Sessions;

namespace Basil.Application.Models.Events.Multiplayer;

/// <summary>Something happened to a room.</summary>
public abstract record RoomEvent(Room Room) : Event;

/// <summary>The room closed and every seated player was evicted.</summary>
public sealed record RoomClosed(Room Room, IReadOnlyList<GameSession> Evicted) : RoomEvent(Room);