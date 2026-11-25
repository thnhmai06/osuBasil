using Basil.Application.Servers.Objects;
using Basil.Application.Sessions.Objects;
using Basil.Domain.Content;
using Mediator;

namespace Basil.Application.Servers.Messages.Content;

public sealed record DeleteMenuSeasonal(Session Actor, string Image) : IRequest<ServerActionResult>;