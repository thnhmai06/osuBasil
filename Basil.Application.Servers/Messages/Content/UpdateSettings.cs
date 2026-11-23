using Basil.Application.Servers.Objects;
using Basil.Application.Shared.Settings;
using Basil.Application.Sessions.Objects;
using Mediator;

namespace Basil.Application.Servers.Messages.Content;

public sealed record UpdateSettings(Session Actor, ServerSettings Settings) : IRequest<ServerActionResult>;