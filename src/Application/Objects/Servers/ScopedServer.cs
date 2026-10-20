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

	/// <summary>Sends a notification to online osu! clients.</summary>
	/// <param name="text">The text to show.</param>
	/// <param name="to">The users to notify, or <see langword="null" /> for every online user.</param>
	/// <returns>
	///     How many clients were sent the notification, or <see langword="null" /> when the caller may not announce.
	/// </returns>
	/// <remarks>Requires <see cref="Permissions.ChatAnnounce" />.</remarks>
	public int? Announce(string text, IReadOnlyCollection<UserRecord>? to = null)
	{
		var context = Server.Context.Session;
		if (By.User.Check(Permissions.ChatAnnounce, context.Clock.GetUtcNow()) is not Access.Allowed) return null;

		var recipients = context.Registry.Online.Values
			.Where(user => to is null || to.Contains(user.Record))
			.Select(user => user.Bancho)
			.Where(connection => connection is not null && connection.IsOpen)
			.Cast<BanchoConnection>()
			.ToList();
		if (recipients.Count == 0) return 0;

		context.Events.Publish(new UserNotificationSent(recipients, text));
		return recipients.Count;
	}
}