using Basil.Application.Common;
using Basil.Application.Users.Objects;
using Basil.Application.Users.Objects.Connections;
using Mediator;

namespace Basil.Application.Users.Messages;

/// <summary>Asks Users to unblock an account for the asking connection's account.</summary>
/// <param name="Actor">The connection that asks.</param>
/// <param name="Target">The account acted on.</param>
public sealed record UnblockUser(Connection Actor, UserReference Target) : IRequest<Result<UserOutcome>>;