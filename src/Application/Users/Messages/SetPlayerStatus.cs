using Basil.Application.Users.Objects.Sessions;
using Basil.Domain.Authentication;
using Mediator;

namespace Basil.Application.Users.Messages;

public sealed record SetPlayerStatus(StableSession Actor, PlayerStatus Status) : IRequest<bool>;