using Basil.Application.Common;
using Basil.Application.Users.Objects;
using Basil.Application.Users.Objects.Connections;
using Mediator;

namespace Basil.Application.Users.Messages;

/// <summary>Asks Users to block an account for the asking connection's account.</summary>
/// <param name="Actor">The connection that asks.</param>
/// <param name="Target">The account acted on.</param>
public sealed record BlockUser(Connection Actor, UserReference Target) : IRequest<Result<UserOutcome>>;