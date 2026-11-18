using Basil.Application.Diagnostics.Objects;
using Basil.Application.Users.Objects.Connections;
using Mediator;

namespace Basil.Application.Diagnostics.Messages;

public sealed record ResetExceptions(Connection Actor) : IRequest<DiagnosticsOutcome>;