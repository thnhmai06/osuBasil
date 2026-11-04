using Basil.Application.Users.Objects.Connections;
using Mediator;

namespace Basil.Application.Multiplayer.Messages;

/// <summary>Asks Multiplayer to stop sending a connection the lobby's room list.</summary>
/// <param name="Actor">The osu! client that stops watching the lobby.</param>
/// <remarks>Asking again does nothing.</remarks>
public sealed record UnwatchLobby(Connection Actor) : IRequest;
