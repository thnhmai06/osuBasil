using Basil.Application.Users.Objects;
using Basil.Application.Users.Objects.Sessions;
using Mediator;

namespace Basil.Application.Users.Messages;

public sealed record SetUserAway(Session Actor, UserReference Target, string? Message) : IRequest<UserOutcome>;