using Basil.Application.Servers.Objects;
using Basil.Application.Users.Objects.Sessions;
using Mediator;

namespace Basil.Application.Servers.Messages;

public sealed record NotifyEveryone(Session Actor, string Text) : IRequest<(ServerOutcome Result, int Sent)>;