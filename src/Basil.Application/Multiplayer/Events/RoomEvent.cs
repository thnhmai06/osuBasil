using Basil.Application.Common.Events;
using Basil.Application.Multiplayer;
using Basil.Application.Sessions;
namespace Basil.Application.Multiplayer.Events;

/// <summary>Something happened to a room.</summary>
public abstract record RoomEvent(Room Room) : Event;

/// <summary>The room closed and every seated player was evicted.</summary>
public sealed record RoomClosed(Room Room, IReadOnlyList<GameSession> Evicted) : RoomEvent(Room);