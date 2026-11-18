using Basil.Application.Users.Objects.Sessions;
using Basil.Domain.Game.Client;
using Basil.Domain.Game.Spectating;
using Mediator;

namespace Basil.Application.Users.Messages;

public sealed record PublishSpectatorFrames(StableSession Actor, SpectatorBundle Bundle) : IRequest<bool>;