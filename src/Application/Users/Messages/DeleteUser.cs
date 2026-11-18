using Basil.Application.Users.Objects;
using Basil.Application.Users.Objects.Connections;
using Mediator;

namespace Basil.Application.Users.Messages;

public sealed record DeleteUser(Connection Actor, UserReference Target) : IRequest<UserOutcome>;