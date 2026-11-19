using Basil.Application.Servers.Objects;
using Basil.Application.Users.Objects.Sessions;
using Mediator;

namespace Basil.Application.Servers.Messages.Content;

public sealed record SaveFaq(Session Actor, string Entry, string Text) : IRequest<ServerActionResult>;