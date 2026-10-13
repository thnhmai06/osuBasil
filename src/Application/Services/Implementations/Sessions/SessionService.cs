using System.Threading.Channels;
using Basil.Application.Services.Contracts.Sessions;
using Basil.Application.Services.Implementations.Users;
using Basil.Application.Storage.Contracts.Sessions;
using Basil.Domain.Users;

namespace Basil.Application.Services.Implementations.Sessions;

/// <summary>Opens and closes the connections of online users and changes what they share; connections, presence and spectating are its parts.</summary>
internal sealed class SessionService(
	SessionConnectionsService connections,
	SessionPresenceService presence,
	SessionSpectatorsService spectators,
	SessionEventStream events,
	IUserRegistry registry,
	TimeProvider time) : ISessionService
{
	/// <inheritdoc />
	public ChannelReader<UserEvent> Events => events.Reader;

	/// <inheritdoc />
	public ISessionConnectionsService Connections => connections;

	/// <inheritdoc />
	public ISessionPresenceService Presence => presence;

	/// <inheritdoc />
	public ISessionSpectatorsService Spectators => spectators;

	/// <inheritdoc />
	public int? Announce(Connection by, string text, IReadOnlyCollection<User>? to = null)
	{
		if (!PermissionRules.Allows(by, Permissions.ModeratorAnnounce, time.GetUtcNow())) return null;

		var recipients = registry.Sessions.Values
			.Where(s => to is null || to.Contains(s.User))
			.Select(s => s.Bancho)
			.Where(c => c is not null && c.IsOpen)
			.Cast<BanchoConnection>()
			.ToList();

		if (recipients.Count == 0)
			return 0;

		events.Emit(new UserNotificationSent(recipients, text));
		return recipients.Count;
	}
}
