using Basil.Application.Users.Objects;
using Basil.Application.Users.Objects.Connections;
using Mediator;

namespace Basil.Application.Users.Messages;

public sealed record SetUserPmPrivate(Connection Actor, UserReference Target, bool PmPrivate) : IRequest<UserOutcome>;