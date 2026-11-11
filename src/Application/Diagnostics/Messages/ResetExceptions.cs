using Basil.Application.Common;
using Basil.Application.Diagnostics.Objects;
using Basil.Application.Users.Objects.Connections;
using Mediator;

namespace Basil.Application.Diagnostics.Messages;

/// <summary>Asks Diagnostics to forget the counted exceptions with the privileges of the sender.</summary>
/// <param name="Actor">The connection acting.</param>
public sealed record ResetExceptions(Connection Actor) : IRequest<DiagnosticsOutcome>;