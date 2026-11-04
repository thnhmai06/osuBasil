using Basil.Application.Common;
using Basil.Application.Users.Objects;
using Basil.Application.Users.Objects.Connections;
using Basil.Domain.Users;
using Mediator;

namespace Basil.Application.Users.Messages;

/// <summary>Asks Users for the friends of the account a connection belongs to.</summary>
/// <param name="Actor">The connection acting for its own account.</param>
public sealed record GetFriends(Connection Actor)
	: IRequest<(Result<UserOutcome> Result, IReadOnlyList<UserRecord> Users)>;
