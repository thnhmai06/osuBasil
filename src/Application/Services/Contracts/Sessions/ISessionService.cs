using Basil.Application.Services.Contracts.Events;
using Basil.Application.Storage.Contracts.Sessions;
using Basil.Domain.Users;

namespace Basil.Application.Services.Contracts.Sessions;

/// <summary>
///     Opens and closes the connections of online users and changes what they share; connections and presence are its
///     parts.
/// </summary>
public interface ISessionService : IEventPublisher<UserEvent>
{
	/// <summary>Connections: closing them, keeping them alive, acting for another user.</summary>
	ISessionConnectionsService Connections { get; }

	/// <summary>What online users show to others.</summary>
	ISessionPresenceService Presence { get; }

	/// <summary>Shows a notification to online osu! clients.</summary>
	/// <param name="by">The connection asking; its user needs <see cref="Permissions.ChatAnnounce" />.</param>
	/// <param name="text">The text to show.</param>
	/// <param name="to">The users to notify, or <see langword="null" /> for every online user.</param>
	/// <returns>
	///     How many clients were sent the notification, or <see langword="null" /> when the caller may not announce.
	/// </returns>
	int? Announce(Connection by, string text, IReadOnlyCollection<User>? to = null);
}