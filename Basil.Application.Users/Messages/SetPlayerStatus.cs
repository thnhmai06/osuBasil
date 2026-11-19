using Basil.Application.Users.Objects.Sessions;
using Basil.Domain.Users;
using Mediator;

namespace Basil.Application.Users.Messages;

public sealed record SetPlayerStatus(StableSession Actor, UserStatus Status) : IRequest<bool>;