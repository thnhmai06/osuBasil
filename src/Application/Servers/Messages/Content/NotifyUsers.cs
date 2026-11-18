using Basil.Application.Servers.Objects;
using Basil.Application.Users.Objects.Connections;
using Mediator;

namespace Basil.Application.Servers.Messages;

public sealed record NotifyUsers(Connection Actor, string Text, IReadOnlyCollection<int> To)
	: IRequest<(ServerOutcome Result, int Sent)>;