using Basil.Application.Servers.Objects;
using Basil.Application.Shared.Settings;
using Basil.Application.Users.Objects.Sessions;
using Mediator;

namespace Basil.Application.Servers.Messages.Content;

public sealed record GetSettings(Session Actor) : IRequest<(ServerActionResult Result, ServerSettings? Settings)>;