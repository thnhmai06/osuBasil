using Basil.Application.Users.Objects;
using Basil.Application.Users.Objects.Connections;
using Basil.Domain.Authentication;
using Mediator;

namespace Basil.Application.Users.Messages;

public sealed record GetUserSession(Connection Actor, UserReference Target, long SessionId)
	: IRequest<(UserOutcome Result, SessionRecord? Session)>;