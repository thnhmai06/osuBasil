using Basil.Application.Servers.Objects;
using Basil.Application.Shared.Settings;
using Basil.Application.Users.Objects.Connections;
using Mediator;

namespace Basil.Application.Servers.Messages;

public sealed record GetSettings(Connection Actor) : IRequest<(ServerOutcome Result, ServerSettings? Settings)>;