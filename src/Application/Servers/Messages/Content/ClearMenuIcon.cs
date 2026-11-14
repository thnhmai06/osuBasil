using Basil.Application.Servers.Objects;
using Basil.Application.Users.Objects.Connections;
using Mediator;

namespace Basil.Application.Servers.Messages;

/// <summary>Asks Servers to remove the main-menu icon.</summary>
/// <param name="Actor">The connection acting.</param>
public sealed record ClearMenuIcon(Connection Actor) : IRequest<ServerOutcome>;