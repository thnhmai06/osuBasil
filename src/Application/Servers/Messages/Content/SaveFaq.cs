using Basil.Application.Servers.Objects;
using Basil.Application.Users.Objects.Sessions;
using Mediator;

namespace Basil.Application.Servers.Messages;

public sealed record SaveFaq(Session Actor, string Entry, string Text) : IRequest<ServerOutcome>;