using Basil.Application.Sessions.Objects;
using Mediator;

namespace Basil.Application.Multiplayer.Messages;

public sealed record WatchLobby(Session Actor) : IRequest;