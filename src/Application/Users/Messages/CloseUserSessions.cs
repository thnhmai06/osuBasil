using Basil.Application.Common;
using Basil.Application.Users.Objects;
using Basil.Application.Users.Objects.Connections;
using Mediator;

namespace Basil.Application.Users.Messages;

/// <summary>Asks Users to close all the sessions of an account.</summary>
/// <param name="Actor">The connection that asks.</param>
/// <param name="Target">The account acted on.</param>
/// <param name="KeepCurrent">True to keep the session of the asking connection.</param>
public sealed record CloseUserSessions(Connection Actor, UserReference Target, bool KeepCurrent)
	: IRequest<Result<UserOutcome>>;