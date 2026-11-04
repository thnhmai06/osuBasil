using Basil.Application.Common;
using Basil.Application.Servers.Objects;
using Basil.Application.Users.Objects.Connections;
using Basil.Domain.Users;

namespace Basil.Application.Servers.Capabilities;

/// <summary>The server as one connection may act on it.</summary>
public readonly struct ScopedServer
{
	/// <summary>Gets the actor this scope is for.</summary>
	public Connection Actor { get; }

	/// <summary>Gets the permissions the actor held when the scope was created.</summary>
	public UserPermissions Rights { get; }

	/// <summary>Checks whether the actor holds the right an action needs.</summary>
	/// <param name="required">The right the action needs.</param>
	/// <returns><see langword="null" /> when it is held; otherwise what is missing.</returns>
	internal UserPermissions? Require(UserPermissions required)
	{
		return ScopedRights.Require(Rights, required);
	}

	/// <summary>Creates a server scope for a connection.</summary>
	/// <param name="server">The server to scope.</param>
	/// <param name="actor">The connection acting on the server.</param>
	internal ScopedServer(Server server, Connection actor)
	{
		Actor = actor;
		Rights = actor.EffectivePermissions(server.Context.Connections.Clock.GetUtcNow());
		Server = server;
	}

	/// <summary>Gets the server this scope is over.</summary>
	public Server Server { get; }

	/// <summary>Gets operations for general channels.</summary>
	public ScopedServerChannels Channels => new(this);

	/// <summary>Gets operations for the policy settings.</summary>
	public ScopedServerSettings Settings => new(this);

	/// <summary>Gets operations for the content shown to players: FAQ entries and the main menu.</summary>
	public ScopedServerContent Content => new(this);

	/// <summary>Gets operations for screenshots.</summary>
	public ScopedServerScreenshots Screenshots => new(this);

	/// <summary>The beatmapset uploads of this server.</summary>
	public ScopedServerBeatmapsets Beatmapsets => new(this);

	/// <summary>Sends a notification to one user's open osu! client.</summary>
	/// <param name="text">The text to show.</param>
	/// <param name="to">The user to notify.</param>
	/// <returns>Whether the caller may announce and how many clients were sent the notification.</returns>
	/// <remarks>
	///     Requires <see cref="UserPermissions.ChatManageAnnounce" />; only open osu! clients receive the notification.
	/// </remarks>
	public (Result<ServerOutcome> Result, int Sent) Notify(string text, UserRecord to)
	{
		return Denied() is { } denied ? (denied, 0) : (ServerOutcome.Ok, Server.Notify(text, to));
	}

	/// <summary>Sends a notification to the specified users' open osu! clients.</summary>
	/// <param name="text">The text to show.</param>
	/// <param name="to">The users to notify.</param>
	/// <returns>Whether the caller may announce and how many clients were sent the notification.</returns>
	/// <remarks>
	///     Requires <see cref="UserPermissions.ChatManageAnnounce" />; only open osu! clients receive the notification.
	/// </remarks>
	public (Result<ServerOutcome> Result, int Sent) Notify(string text, IReadOnlyCollection<UserRecord> to)
	{
		return Denied() is { } denied ? (denied, 0) : (ServerOutcome.Ok, Server.Notify(text, to));
	}

	/// <summary>Sends a notification to every open osu! client.</summary>
	/// <param name="text">The text to show.</param>
	/// <returns>Whether the caller may announce and how many clients were sent the notification.</returns>
	/// <remarks>
	///     Requires <see cref="UserPermissions.ChatManageAnnounce" />; only open osu! clients receive the notification.
	/// </remarks>
	public (Result<ServerOutcome> Result, int Sent) NotifyAll(string text)
	{
		return Denied() is { } denied ? (denied, 0) : (ServerOutcome.Ok, Server.NotifyAll(text));
	}

	private Result<ServerOutcome>? Denied()
	{
		return Require(UserPermissions.ChatManageAnnounce) is { } missing ? Result<ServerOutcome>.Denied(missing) : null;
	}

	/// <summary>Checks whether the caller holds the permissions an action on this server needs.</summary>
	/// <param name="required">The permissions the action needs.</param>
	/// <returns><see langword="null" /> when all are in effect; otherwise the permissions that are missing.</returns>
	internal UserPermissions? Needs(UserPermissions required)
	{
		return Require(required);
	}
}