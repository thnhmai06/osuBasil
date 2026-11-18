using Basil.Application.Users.Objects;
using Basil.Domain.Users;
using Mediator;

namespace Basil.Application.Users.Messages;

public sealed record GetUser(UserReference Target) : IRequest<UserRecord?>;