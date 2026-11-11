using Basil.Application.Common;
using Basil.Application.Diagnostics.Objects;
using Basil.Application.Users.Objects.Connections;
using Mediator;

namespace Basil.Application.Diagnostics.Messages;

/// <summary>Asks Diagnostics to empty the cache of derived files with the privileges of the sender.</summary>
/// <param name="Actor">The connection acting.</param>
public sealed record ClearCaches(Connection Actor) : IRequest<(Result<DiagnosticsOutcome> Result, int Deleted)>;