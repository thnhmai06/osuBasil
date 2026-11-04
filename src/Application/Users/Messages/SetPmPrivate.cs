using Basil.Application.Users.Objects.Connections;
using Mediator;

namespace Basil.Application.Users.Messages;

/// <summary>Asks Users to set whether the account a connection belongs to accepts private messages only from friends.</summary>
/// <param name="Actor">The connection acting for its own account.</param>
/// <param name="PmPrivate"><see langword="true" /> to accept private messages only from friends.</param>
/// <remarks>The answer is <see langword="true" /> when the setting was set.</remarks>
public sealed record SetPmPrivate(Connection Actor, bool PmPrivate) : IRequest<bool>;
