using Basil.Application.Common;
using Basil.Application.Users.Objects;
using Basil.Application.Users.Objects.Connections;
using Basil.Domain.Users;
using Mediator;

namespace Basil.Application.Users.Messages;

/// <summary>Asks Users to lift a restriction of an account.</summary>
/// <param name="Actor">The connection that asks.</param>
/// <param name="Target">The account acted on.</param>
/// <param name="Restriction">The restriction to lift.</param>
public sealed record LiftRestriction(Connection Actor, UserRecord Target, UserRestriction Restriction)
	: IRequest<Result<UserOutcome>>;