using Basil.Application.Diagnostics.Objects;
using Basil.Application.Sessions.Objects;
using Mediator;

namespace Basil.Application.Diagnostics.Messages;

public sealed record CheckDiagnosticsAccess(Session Actor) : IRequest<DiagnosticsCommandResult>;