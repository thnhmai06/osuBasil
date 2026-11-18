using Basil.Application.Users.Objects;
using Basil.Application.Users.Objects.Connections;
using Mediator;

namespace Basil.Application.Users.Messages;

public sealed record SetUserAway(Connection Actor, UserReference Target, string? Message) : IRequest<UserOutcome>;