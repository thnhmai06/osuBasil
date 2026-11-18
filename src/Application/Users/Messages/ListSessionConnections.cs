using Basil.Application.Shared.Paging;
using Basil.Application.Users.Objects;
using Basil.Application.Users.Objects.Connections;
using Basil.Domain.Authentication;
using Mediator;

namespace Basil.Application.Users.Messages;

public sealed record ListSessionConnections(Connection Actor, UserReference Target, long SessionId, PageRequest Page)
	: IRequest<(UserOutcome Result, Page<ConnectionRecord>? Connections)>;