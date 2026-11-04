using Basil.Application.Users.Objects.Connections;
using Mediator;

namespace Basil.Application.Multiplayer.Messages;

/// <summary>Asks Multiplayer to start sending a connection the lobby's room list.</summary>
/// <param name="Actor">The osu! client that watches the lobby.</param>
public sealed record WatchLobby(Connection Actor) : IRequest;
