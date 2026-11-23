using Basil.Application.Sessions.Objects;
using Basil.Domain.Users;
using Mediator;

namespace Basil.Application.Sessions.Messages;

public sealed record SetPlayerStatus(StableSession Actor, UserStatus Status) : IRequest<bool>;