using Basil.Application.Sessions.Objects;
using Basil.Application.Sessions.Registry;

namespace Basil.Host.Stable.Support;

/// <summary>Chooses the osu! clients that receive a user's presence and stats, asking each client whether it wants them.</summary>
internal sealed class PresenceAudience(SessionRegistry registry)
{
	/// <summary>Returns the clients that want the presence and stats of the user.</summary>
	public IReadOnlyList<StableSession> For(UserSessions user)
	{
		return [.. registry.OpenStableClients().Where(client => client.WantsPresenceOf(user.Record))];
	}
}
