using Basil.Application.Contracts.Events;
using Basil.Application.Storage.Multiplayer;

namespace Basil.Application.Contracts.Multiplayer.Events;

/// <summary>Something happened to a room.</summary>
public abstract record RoomEvent(Room Room) : Event;