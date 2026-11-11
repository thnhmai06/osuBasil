using Basil.Application.Shared;
using Basil.Application.Shared.Settings;
using Basil.Application.Servers.Objects;
using Basil.Application.Users.Objects.Connections;
using Mediator;

namespace Basil.Application.Servers.Messages;

/// <summary>Asks Servers to replace the settings.</summary>
/// <param name="Actor">The connection acting.</param>
/// <param name="Settings">The settings that replace the current ones.</param>
public sealed record UpdateSettings(Connection Actor, ServerSettings Settings) : IRequest<ServerOutcome>;