using Basil.Application.Users.Objects.Connections;
using Basil.Domain.Game.Client;
using Basil.Domain.Game.Spectating;
using Mediator;

namespace Basil.Application.Users.Messages;

public sealed record PublishSpectatorFrames(StableConnection Actor, SpectatorBundle Bundle) : IRequest<bool>;