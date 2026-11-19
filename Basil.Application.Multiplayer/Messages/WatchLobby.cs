using Basil.Application.Users.Objects.Sessions;
using Mediator;

namespace Basil.Application.Multiplayer.Messages;

public sealed record WatchLobby(Session Actor) : IRequest;