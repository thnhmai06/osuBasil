using Basil.Application.Servers.Objects;
using Basil.Application.Users.Objects.Connections;
using Basil.Domain.Content;
using Mediator;

namespace Basil.Application.Servers.Messages;

public sealed record DeleteMenuBanner(Connection Actor, MenuBanner Banner) : IRequest<ServerOutcome>;