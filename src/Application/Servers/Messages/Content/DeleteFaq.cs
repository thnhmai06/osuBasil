using Basil.Application.Servers.Objects;
using Basil.Application.Users.Objects.Connections;
using Mediator;

namespace Basil.Application.Servers.Messages;

public sealed record DeleteFaq(Connection Actor, string Entry) : IRequest<ServerOutcome>;