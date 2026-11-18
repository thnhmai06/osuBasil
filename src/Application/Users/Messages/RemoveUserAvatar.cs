using Basil.Application.Users.Objects;
using Basil.Application.Users.Objects.Sessions;
using Mediator;

namespace Basil.Application.Users.Messages;

public sealed record RemoveUserAvatar(Session Actor, UserReference Target) : IRequest<UserOutcome>;