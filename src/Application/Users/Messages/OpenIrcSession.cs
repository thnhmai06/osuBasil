using Basil.Application.Users.Objects;
using Basil.Application.Users.Objects.Connections;
using Basil.Domain.Authentication;
using Mediator;

namespace Basil.Application.Users.Messages;

public sealed record OpenIrcSession(Connection Actor, UserReference Target, string Label)
	: IRequest<(UserOutcome Result, string? Key, SessionRecord? Session)>;