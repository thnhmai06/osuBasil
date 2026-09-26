using Basil.Application.Models.Notifications;

namespace Basil.Application.Models.Sessions;

/// <summary>
///     A live transport connection to one client, capable of being told what happened via
///     <see cref="Notification" />.
/// </summary>
/// <remarks>
///     <see cref="Send" /> never blocks and preserves send order. When the connection is closed, or
///     its transport has no equivalent for a given <see cref="Notification" />, the send is silently
///     dropped.
/// </remarks>
public interface IClientConnection
{
	/// <summary>Gets a value that indicates whether the connection is currently open.</summary>
	bool IsOpen { get; }

	/// <summary>Sends a notification to the client.</summary>
	/// <param name="notification">The notification to send.</param>
	void Send(Notification notification);
}