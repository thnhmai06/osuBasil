using Basil.Application.Users.Objects.Connections;
using Mediator;

namespace Basil.Application.Users.Messages;

/// <summary>Asks Users to set or clear the away message of the account a connection belongs to.</summary>
/// <param name="Actor">The connection acting for its own account.</param>
/// <param name="Message">The away message, or <see langword="null" /> to clear it.</param>
/// <remarks>The answer is <see langword="true" /> when the message was set.</remarks>
public sealed record SetAwayMessage(Connection Actor, string? Message) : IRequest<bool>;