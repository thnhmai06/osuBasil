using Basil.Application.Users.Objects.Sessions;
using Mediator;

namespace Basil.Application.Users.Messages;

public sealed record StopSpectating(Session Actor, StableSession Host) : IRequest<bool>;