using Basil.Application.Users.Objects;
using Basil.Application.Users.Objects.Connections;
using Mediator;

namespace Basil.Application.Users.Messages;

/// <summary>Asks Users to remove an account from the friends of the asking connection's account.</summary>
/// <param name="Actor">The connection that asks.</param>
/// <param name="Target">The account acted on.</param>
public sealed record RemoveFriend(Connection Actor, UserReference Target) : IRequest<UserOutcome>;