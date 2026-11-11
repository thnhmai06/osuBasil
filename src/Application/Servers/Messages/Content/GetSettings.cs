using Basil.Application.Common;
using Basil.Application.Common.Settings;
using Basil.Application.Servers.Objects;
using Basil.Application.Users.Objects.Connections;
using Mediator;

namespace Basil.Application.Servers.Messages;

/// <summary>Asks Servers for the settings in force.</summary>
/// <param name="Actor">The connection acting.</param>
public sealed record GetSettings(Connection Actor) : IRequest<(ServerOutcome Result, ServerSettings? Settings)>;