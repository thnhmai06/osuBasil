using Basil.Application.Servers.Objects;
using Basil.Application.Users.Objects.Sessions;
using Mediator;

namespace Basil.Application.Servers.Messages;

public sealed record DeleteFaq(Session Actor, string Entry) : IRequest<ServerOutcome>;