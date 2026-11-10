using Basil.Application.Users.Objects;
using Basil.Application.Users.Objects.Connections;
using Mediator;

namespace Basil.Application.Users.Messages;

/// <summary>Asks Users to set whether an account accepts private messages only from friends.</summary>
/// <param name="Actor">The connection that asks.</param>
/// <param name="Target">The account acted on.</param>
/// <param name="PmPrivate">True to accept private messages only from friends.</param>
public sealed record SetUserPmPrivate(Connection Actor, UserReference Target, bool PmPrivate) : IRequest<bool>;