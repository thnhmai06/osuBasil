using Basil.Application.Users.Objects;
using Basil.Application.Users.Objects.Connections;
using Mediator;

namespace Basil.Application.Users.Messages;

/// <summary>Asks Users to lift the silence of an account.</summary>
/// <param name="Actor">The connection that asks.</param>
/// <param name="Target">The account acted on.</param>
/// <param name="Reason">Why the silence is lifted, if known.</param>
public sealed record UnsilenceUser(Connection Actor, UserReference Target, string? Reason)
	: IRequest<UserOutcome>;