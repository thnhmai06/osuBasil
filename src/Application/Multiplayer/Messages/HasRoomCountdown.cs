using Basil.Application.Multiplayer.Objects.Rooms;
using Basil.Application.Users.Objects.Connections;
using Mediator;

namespace Basil.Application.Multiplayer.Messages;

/// <summary>Asks whether a countdown is running in a room.</summary>
/// <param name="Actor">The connection that asks.</param>
/// <param name="Room">The room.</param>
/// <remarks>The answer is <see langword="true" /> while a countdown runs, until it elapses or is cancelled.</remarks>
public sealed record HasRoomCountdown(Connection Actor, Room Room) : IRequest<bool>;
