using Basil.Application.Sessions.Objects;
using Basil.Application.Sessions.Registry;
using Basil.Application.Users.Messages;
using Basil.Domain.Game;
using Mediator;

namespace Basil.Host.Stable.Support;

/// <summary>Chooses the osu! clients that receive a user's presence and stats, following each client's <see cref="PresenceFilter"/>.</summary>
internal sealed class PresenceAudience(SessionRegistry registry, ISender sender)
{
	/// <summary>Returns the clients that asked for everyone, the clients that asked for friends and have the user as one, and the user's own clients.</summary>
	public async Task<IReadOnlyList<StableSession>> ForAsync(UserSessions user, CancellationToken ct)
	{
		var audience = new List<StableSession>();
		foreach (var client in registry.OpenStableClients())
		{
			if (client.Record.Equals(user.Record) || client.PresenceFilter is PresenceFilter.All)
				audience.Add(client);
			else if (client.PresenceFilter is PresenceFilter.Friends &&
			         (await sender.Send(new GetFriends(client), ct)).Users.Any(friend => friend.Equals(user.Record)))
				audience.Add(client);
		}

		return audience;
	}
}
