using Basil.Application.Users.Objects;
using Basil.Application.Users.Objects.Connections;
using Mediator;

namespace Basil.Application.Users.Messages;

public sealed record RemoveFriend(Connection Actor, UserReference Target) : IRequest<UserOutcome>;