using Basil.Application.Diagnostics.Objects;
using Basil.Application.Users.Objects.Sessions;
using Mediator;

namespace Basil.Application.Diagnostics.Messages;

public sealed record CollectGarbage(Session Actor) : IRequest<DiagnosticsOutcome>;