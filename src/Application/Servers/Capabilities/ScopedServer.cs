using Basil.Application.Servers.Objects;
using Basil.Application.Shared;
using Basil.Application.Shared.Privileges;
using Basil.Application.Users.Objects.Connections;
using Basil.Domain.Users;

namespace Basil.Application.Servers.Capabilities;

/// <summary>The server as one connection may act on it.</summary>
public readonly struct ScopedServer
{
	/// <summary>Gets the actor this scope is for.</summary>
	public Connection Actor { get; }

	/// <summary>Gets the privileges the actor held when the scope was created.</summary>
	public UserPrivileges Privileges { get; }

	/// <summary>Checks whether the actor holds the privilege an action needs.</summary>
	/// <param name="required">The privilege the action needs.</param>
	/// <returns><see langword="null" /> when it is held; otherwise what is missing.</returns>
	internal bool Lacks(UserPrivileges required)
	{
		return ScopedPrivileges.Lacks(Privileges, required);
	}

	/// <summary>Creates a server scope for a connection.</summary>
	/// <param name="server">The server to scope.</param>
	/// <param name="actor">The connection acting on the server.</param>
	internal ScopedServer(Server server, Connection actor)
	{
		Actor = actor;
		Privileges = actor.EffectivePrivileges(server.Context.Connections.Clock.GetUtcNow());
		Server = server;
	}

	/// <summary>Gets the server this scope is over.</summary>
	public Server Server { get; }

	/// <summary>Gets operations for general channels.</summary>
	public ScopedServerChannels Channels => new(this);

	/// <summary>Gets operations for the policy settings.</summary>
	public ScopedServerSettings Settings => new(this);

	/// <summary>Gets the FAQ entries as the actor may change them.</summary>
	public ScopedServerFaq Faq => new(this);

	/// <summary>Gets the main-menu banners as the actor may change them.</summary>
	public ScopedServerBanners Banners => new(this);

	/// <summary>Gets the seasonal backgrounds as the actor may change them.</summary>
	public ScopedServerSeasonals Seasonals => new(this);

	/// <summary>Gets the main-menu icon as the actor may change them.</summary>
	public ScopedServerIcon Icon => new(this);

	/// <summary>Gets operations for screenshots.</summary>
	public ScopedServerScreenshots Screenshots => new(this);

	/// <summary>The beatmapset uploads of this server.</summary>
	public ScopedServerBeatmapsets Beatmapsets => new(this);

	/// <summary>Sends a notification to one user's open osu! client.</summary>
	/// <param name="text">The text to show.</param>
	/// <param name="to">The user to notify.</param>
	/// <returns>Whether the caller may announce and how many clients were sent the notification.</returns>
	/// <remarks>
	///     Requires <see cref="UserPrivileges.Moderator" />; only open osu! clients receive the notification.
	/// </remarks>
	public (ServerOutcome Result, int Sent) Notify(string text, UserRecord to)
	{
		return Denied() is { } denied ? (denied, 0) : (ServerOutcome.Ok, Server.Notify(text, to));
	}

	/// <summary>Sends a notification to the specified users' open osu! clients.</summary>
	/// <param name="text">The text to show.</param>
	/// <param name="to">The ids of the users to notify.</param>
	/// <returns>Whether the caller may announce and how many clients were sent the notification.</returns>
	/// <remarks>
	///     Requires <see cref="UserPrivileges.Moderator" />; only open osu! clients receive the notification.
	/// </remarks>
	public (ServerOutcome Result, int Sent) Notify(string text, IReadOnlyCollection<int> to)
	{
		return Denied() is { } denied ? (denied, 0) : (ServerOutcome.Ok, Server.Notify(text, to));
	}

	/// <summary>Sends a notification to every open osu! client.</summary>
	/// <param name="text">The text to show.</param>
	/// <returns>Whether the caller may announce and how many clients were sent the notification.</returns>
	/// <remarks>
	///     Requires <see cref="UserPrivileges.Moderator" />; only open osu! clients receive the notification.
	/// </remarks>
	public (ServerOutcome Result, int Sent) NotifyAll(string text)
	{
		return Denied() is { } denied ? (denied, 0) : (ServerOutcome.Ok, Server.NotifyAll(text));
	}

	private ServerOutcome? Denied()
	{
		return Lacks(UserPrivileges.Moderator)
			? ServerOutcome.Denied
			: null;
	}

}