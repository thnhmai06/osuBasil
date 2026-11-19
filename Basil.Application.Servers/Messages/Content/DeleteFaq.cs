using Basil.Application.Servers.Objects;
using Basil.Application.Users.Objects.Sessions;
using Mediator;

namespace Basil.Application.Servers.Messages.Content;

public sealed record DeleteFaq(Session Actor, string Entry) : IRequest<ServerActionResult>;