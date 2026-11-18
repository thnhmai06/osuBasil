using Basil.Application.Users.Objects;
using Basil.Application.Users.Objects.Connections;
using Mediator;

namespace Basil.Application.Users.Messages;

public sealed record SetUserAvatar(Connection Actor, UserReference Target, Stream Image) : IRequest<UserOutcome>;