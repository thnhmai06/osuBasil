using Basil.Application.Users.Objects.Sessions;
using Basil.Domain.Users;
using Mediator;

namespace Basil.Application.Users.Messages;

public sealed record SetUserPmPrivate(Session Actor, UserReference Target, bool PmPrivate) : IRequest<UserActionResult>;