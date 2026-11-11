using Basil.Application.Diagnostics.Objects;
using Basil.Application.Shared;
using Basil.Application.Users.Objects.Connections;
using Mediator;

namespace Basil.Application.Diagnostics.Messages;

/// <summary>Asks Diagnostics to collect the garbage now with the privileges of the sender.</summary>
/// <param name="Actor">The connection acting.</param>
public sealed record CollectGarbage(Connection Actor) : IRequest<DiagnosticsOutcome>;