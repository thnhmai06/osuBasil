using Basil.Application.Servers.Objects;
using Basil.Application.Users.Objects.Connections;
using Mediator;

namespace Basil.Application.Servers.Messages;

public sealed record NotifyEveryone(Connection Actor, string Text) : IRequest<(ServerOutcome Result, int Sent)>;