using Basil.Application.Sessions.Objects;
using Mediator;

namespace Basil.Application.Sessions.Messages;

public sealed record StopSpectating(Session Actor, StableSession Host) : IRequest<bool>;