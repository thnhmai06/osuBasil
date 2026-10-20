using Basil.Application.Objects.Sessions;
using Basil.Domain.Permissions;
using Basil.Domain.Users;

namespace Basil.Application.Objects.Servers;

/// <summary>Server operations scoped to one connection.</summary>
public sealed class ScopedServer
{
	/// <summary>The server this scope is over.</summary>
	public Server Server { get; }

	/// <summary>The connection this scope is for.</summary>
	public Connection By { get; }

	/// <summary>Creates a server scope for a connection.</summary>
	/// <param name="server">The server to scope.</param>
	/// <param name="by">The connection acting on the server.</param>
	internal ScopedServer(Server server, Connection by)
	{
		Server = server;
		By = by;
		Channels = new ScopedServerChannels(this);
		Screenshots = new ScopedServerScreenshots(this);
	}

	/// <summary>Gets operations for general channels.</summary>
	public ScopedServerChannels Channels { get; }

	/// <summary>Gets operations for screenshots.</summary>
	public ScopedServerScreenshots Screenshots { get; }

	/// <summary>Sends a notification to one user's open osu! client.</summary>
	/// <param name="text">The text to show.</param>
	/// <param name="to">The user to notify.</param>
	/// <returns>
	///     The number of clients sent the notification, or <see langword="null" /> when the caller lacks permission.
	/// </returns>
	/// <remarks>
	///     Requires <see cref="Permissions.ChatAnnounce" />; only open osu! clients receive the notification.
	/// </remarks>
	public int? Notify(string text, UserRecord to)
	{
		return CanAnnounce() ? Server.Notify(text, to) : null;
	}

	/// <summary>Sends a notification to the specified users' open osu! clients.</summary>
	/// <param name="text">The text to show.</param>
	/// <param name="to">The users to notify.</param>
	/// <returns>
	///     The number of clients sent the notification, or <see langword="null" /> when the caller lacks permission.
	/// </returns>
	/// <remarks>
	///     Requires <see cref="Permissions.ChatAnnounce" />; only open osu! clients receive the notification.
	/// </remarks>
	public int? Notify(string text, IReadOnlyCollection<UserRecord> to)
	{
		return CanAnnounce() ? Server.Notify(text, to) : null;
	}

	/// <summary>Sends a notification to every open osu! client.</summary>
	/// <param name="text">The text to show.</param>
	/// <returns>
	///     The number of clients sent the notification, or <see langword="null" /> when the caller lacks permission.
	/// </returns>
	/// <remarks>
	///     Requires <see cref="Permissions.ChatAnnounce" />; only open osu! clients receive the notification.
	/// </remarks>
	public int? NotifyAll(string text)
	{
		return CanAnnounce() ? Server.NotifyAll(text) : null;
	}

	private bool CanAnnounce()
	{
		var context = Server.Context.Session;
		return By.User.Check(Permissions.ChatAnnounce, context.Clock.GetUtcNow()) is Access.Allowed;
	}
}