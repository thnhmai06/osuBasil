using Basil.Application.Servers.Objects;
using Basil.Application.Users.Objects.Connections;
using Mediator;

namespace Basil.Application.Servers.Messages;

public sealed record SaveFaq(Connection Actor, string Entry, string Text) : IRequest<ServerOutcome>;