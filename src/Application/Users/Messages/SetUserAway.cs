using Basil.Application.Users.Objects;
using Basil.Application.Users.Objects.Connections;
using Mediator;

namespace Basil.Application.Users.Messages;

/// <summary>Asks Users to set or clear the away message of an account.</summary>
/// <param name="Actor">The connection that asks.</param>
/// <param name="Target">The account acted on.</param>
/// <param name="Message">The away message, or null to clear it.</param>
public sealed record SetUserAway(Connection Actor, UserReference Target, string? Message) : IRequest<bool>;