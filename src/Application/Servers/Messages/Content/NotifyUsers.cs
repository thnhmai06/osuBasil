using Basil.Application.Common;
using Basil.Application.Servers.Objects;
using Basil.Application.Users.Objects.Connections;
using Basil.Domain.Users;
using Mediator;

namespace Basil.Application.Servers.Messages;

/// <summary>Asks Servers to notify the open osu! clients of the given users.</summary>
/// <param name="Actor">The connection acting.</param>
public sealed record NotifyUsers(Connection Actor, string Text, IReadOnlyCollection<UserRecord> To)
	: IRequest<(Result<ServerOutcome> Result, int Sent)>;