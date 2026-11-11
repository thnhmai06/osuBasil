using Basil.Application.Common;
using Basil.Application.Users.Objects;
using Basil.Application.Users.Objects.Connections;
using Basil.Domain.Authentication;
using Mediator;

namespace Basil.Application.Users.Messages;

/// <summary>Asks Users for the connections of one session of an account.</summary>
/// <param name="Actor">The connection that asks.</param>
/// <param name="Target">The account acted on.</param>
/// <param name="SessionId">The session whose connections to read.</param>
/// <param name="Page">Which part of the trail to return.</param>
public sealed record ListSessionConnections(Connection Actor, UserReference Target, long SessionId, PageRequest Page)
	: IRequest<(UserOutcome Result, Page<ConnectionRecord>? Connections)>;
