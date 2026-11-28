using Basil.Application.Servers.Objects;
using Basil.Application.Sessions.Objects;
using Mediator;

namespace Basil.Application.Servers.Messages.Content;

/// <summary>Asks to send a notification to some users by their ids.</summary>
/// <param name="Actor">The session asking.</param>
/// <param name="Text">The text of the notification.</param>
/// <param name="To">The ids of the users; a repeated id counts once.</param>
/// <remarks>The answer is the number of clients that received it, and the ids that name no user; when any id names no user nothing is sent.</remarks>
public sealed record NotifyUsers(Session Actor, string Text, IReadOnlyCollection<int> To)
	: IRequest<(ServerActionResult Result, int Sent, IReadOnlyList<int> Missing)>;