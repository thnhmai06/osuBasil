using Basil.Application.Servers.Objects;
using Basil.Application.Users.Objects.Sessions;
using Basil.Domain.Content;
using Mediator;

namespace Basil.Application.Servers.Messages;

public sealed record DeleteMenuBanner(Session Actor, MenuBanner Banner) : IRequest<ServerOutcome>;