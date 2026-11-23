using Basil.Application.Sessions.Objects;
using Basil.Domain.Spectating;
using Mediator;

namespace Basil.Application.Sessions.Messages;

public sealed record PublishSpectatorFrames(StableSession Actor, SpectatorBundle Bundle) : IRequest<bool>;