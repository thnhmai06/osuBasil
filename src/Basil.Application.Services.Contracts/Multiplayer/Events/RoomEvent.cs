using Basil.Application.Services.Contracts.Events;
using Basil.Application.Storage.Contracts.Multiplayer;

namespace Basil.Application.Services.Contracts.Multiplayer.Events;

/// <summary>Something happened to a room.</summary>
public abstract record RoomEvent(Room Room) : Event;