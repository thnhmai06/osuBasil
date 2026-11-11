using Basil.Application.Servers.Objects;
using Basil.Application.Shared;
using Basil.Application.Users.Objects.Connections;
using Mediator;

namespace Basil.Application.Servers.Messages;

/// <summary>Asks Servers to notify the open osu! clients of the given users.</summary>
/// <param name="Actor">The connection acting.</param>
/// <param name="Text">The text of the notice.</param>
/// <param name="To">The ids of the users to notify; an id of no online user is skipped.</param>
public sealed record NotifyUsers(Connection Actor, string Text, IReadOnlyCollection<int> To)
	: IRequest<(ServerOutcome Result, int Sent)>;