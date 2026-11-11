using Basil.Application.Shared;
using Basil.Application.Users.Objects;
using Basil.Application.Users.Objects.Connections;
using Basil.Domain.Authentication;
using Mediator;

namespace Basil.Application.Users.Messages;

/// <summary>Asks Users to open an IRC session for an account.</summary>
/// <param name="Actor">The connection that asks.</param>
/// <param name="Target">The account acted on.</param>
/// <param name="Label">The label of the new session.</param>
public sealed record OpenIrcSession(Connection Actor, UserReference Target, string Label)
	: IRequest<(UserOutcome Result, string? Key, SessionRecord? Session)>;