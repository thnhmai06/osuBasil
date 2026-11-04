using Basil.Application.Common;
using Basil.Application.Servers.Objects;
using Basil.Application.Users.Objects.Connections;
using Mediator;

namespace Basil.Application.Servers.Messages;

/// <summary>Asks Servers whether the sender may use the diagnostics.</summary>
/// <param name="Actor">The connection acting.</param>
public sealed record CheckDiagnosticsAccess(Connection Actor) : IRequest<Result<ServerOutcome>>;
