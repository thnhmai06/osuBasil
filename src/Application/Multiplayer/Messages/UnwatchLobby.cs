using Basil.Application.Users.Objects.Connections;
using Mediator;

namespace Basil.Application.Multiplayer.Messages;

public sealed record UnwatchLobby(Connection Actor) : IRequest;