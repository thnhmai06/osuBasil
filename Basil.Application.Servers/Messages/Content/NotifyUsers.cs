using Basil.Application.Servers.Objects;
using Basil.Application.Users.Objects.Sessions;
using Mediator;

namespace Basil.Application.Servers.Messages.Content;

public sealed record NotifyUsers(Session Actor, string Text, IReadOnlyCollection<int> To)
	: IRequest<(ServerOutcome Result, int Sent)>;