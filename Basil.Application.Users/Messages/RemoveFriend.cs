using Basil.Application.Users.Objects.Sessions;
using Basil.Domain.Users;
using Mediator;

namespace Basil.Application.Users.Messages;

public sealed record RemoveFriend(Session Actor, UserReference Target) : IRequest<UserActionResult>;