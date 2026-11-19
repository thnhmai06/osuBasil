using Basil.Application.Servers.Objects;
using Basil.Application.Users.Objects.Sessions;
using Basil.Domain.Content;
using Mediator;

namespace Basil.Application.Servers.Messages.Content;

public sealed record DeleteMenuSeasonal(Session Actor, ContentReference Image) : IRequest<ServerOutcome>;