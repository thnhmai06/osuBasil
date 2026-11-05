using Basil.Application.Common;
using Basil.Application.Diagnostics.Objects;
using Basil.Application.Users.Objects.Connections;
using Mediator;

namespace Basil.Application.Diagnostics.Messages;

/// <summary>Asks Diagnostics whether the sender may use the diagnostics.</summary>
/// <param name="Actor">The connection acting.</param>
public sealed record CheckDiagnosticsAccess(Connection Actor) : IRequest<Result<DiagnosticsOutcome>>;