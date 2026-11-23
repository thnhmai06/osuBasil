using Basil.Application.Diagnostics.Objects;
using Basil.Application.Sessions.Objects;
using Mediator;

namespace Basil.Application.Diagnostics.Messages;

public sealed record ClearCaches(Session Actor) : IRequest<(DiagnosticsCommandResult Result, int Deleted)>;