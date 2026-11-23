using Basil.Application.Servers.Objects;
using Basil.Application.Sessions.Objects;
using Mediator;

namespace Basil.Application.Servers.Messages.Content;

public sealed record DeleteFaq(Session Actor, string Entry) : IRequest<ServerActionResult>;