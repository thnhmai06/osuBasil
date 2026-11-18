using Basil.Application.Users.Objects.Connections;
using Basil.Domain.Client;
using Mediator;

namespace Basil.Application.Users.Messages;

public sealed record PublishSpectatorFrames(StableConnection Actor, SpectatorBundle Bundle) : IRequest<bool>;