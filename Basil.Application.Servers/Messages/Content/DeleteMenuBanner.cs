using Basil.Application.Servers.Objects;
using Basil.Application.Sessions.Objects;
using Basil.Domain.Content;
using Mediator;

namespace Basil.Application.Servers.Messages.Content;

public sealed record DeleteMenuBanner(Session Actor, string Image) : IRequest<ServerActionResult>;