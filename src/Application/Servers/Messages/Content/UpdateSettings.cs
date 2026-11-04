using Basil.Application.Common;
using Basil.Application.Content.Persistence;
using Basil.Application.Servers.Objects;
using Basil.Application.Users.Objects.Connections;
using Mediator;

namespace Basil.Application.Servers.Messages;

/// <summary>Asks Servers to replace the settings.</summary>
/// <param name="Actor">The connection acting.</param>
public sealed record UpdateSettings(Connection Actor, ServerSettings Settings) : IRequest<Result<ServerOutcome>>;
