using Basil.Application.Users.Objects.Connections;
using Mediator;

namespace Basil.Application.Users.Messages;

public sealed record ReportSpectatingFailed(Connection Actor, StableConnection Host) : IRequest<bool>;