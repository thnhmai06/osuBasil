using Basil.Application.Common;
using Basil.Application.Users.Objects;
using Basil.Application.Users.Objects.Connections;
using Basil.Domain.Users;
using Mediator;

namespace Basil.Application.Users.Messages;

/// <summary>Asks Users for the friends of an account.</summary>
/// <param name="Actor">The connection that asks.</param>
/// <param name="Target">The account acted on.</param>
public sealed record ListFriends(Connection Actor, UserRecord Target)
	: IRequest<(Result<UserOutcome> Result, IReadOnlyList<UserRecord> Users)>;