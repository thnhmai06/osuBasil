using Basil.Application.Common;
using Basil.Application.Common.Common;
using Basil.Application.Users.Connections;
using Basil.Domain.Users;

namespace Basil.Application.Servers.Runtime;

/// <summary>The server as one connection may act on it.</summary>
public sealed class ScopedServer : Scoped<UserPermissions, Connection>
{
	/// <summary>Creates a server scope for a connection.</summary>
	/// <param name="server">The server to scope.</param>
	/// <param name="by">The connection acting on the server.</param>
	internal ScopedServer(Server server, Connection by) : base(by,
		by.EffectivePermissions(server.Context.Connections.Clock.GetUtcNow()))
	{
		Server = server;
		Channels = new ScopedServerChannels(this);
		Screenshots = new ScopedServerScreenshots(this);
		Beatmapsets = new ScopedServerBeatmapsets(this);
		Settings = new ScopedServerSettings(this);
		Diagnostics = new ScopedServerDiagnostics(this);
		Content = new ScopedServerContent(this);
	}

	/// <summary>Gets the server this scope is over.</summary>
	public Server Server { get; }

	/// <summary>Gets operations for general channels.</summary>
	public ScopedServerChannels Channels { get; }

	/// <summary>Gets operations for the policy settings.</summary>
	public ScopedServerSettings Settings { get; }

	/// <summary>Gets operations for the content shown to players: FAQ entries and the main menu.</summary>
	public ScopedServerContent Content { get; }

	/// <summary>Gets the access check of the diagnostics.</summary>
	public ScopedServerDiagnostics Diagnostics { get; }

	/// <summary>Gets operations for screenshots.</summary>
	public ScopedServerScreenshots Screenshots { get; }

	/// <summary>The beatmapset uploads of this server.</summary>
	public ScopedServerBeatmapsets Beatmapsets { get; }

	/// <summary>Sends a notification to one user's open osu! client.</summary>
	/// <param name="text">The text to show.</param>
	/// <param name="to">The user to notify.</param>
	/// <returns>Whether the caller may announce and how many clients were sent the notification.</returns>
	/// <remarks>
	///     Requires <see cref="UserPermissions.ChatManageAnnounce" />; only open osu! clients receive the notification.
	/// </remarks>
	public (Result<ServerOutcome> Result, int Sent) Notify(string text, UserRecord to)
	{
		return Announce(() => Server.Notify(text, to));
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
		return Announce(() => Server.Notify(text, to));
	}

	/// <summary>Sends a notification to every open osu! client.</summary>
	/// <param name="text">The text to show.</param>
	/// <returns>Whether the caller may announce and how many clients were sent the notification.</returns>
	/// <remarks>
	///     Requires <see cref="UserPermissions.ChatManageAnnounce" />; only open osu! clients receive the notification.
	/// </remarks>
	public (Result<ServerOutcome> Result, int Sent) NotifyAll(string text)
	{
		return Announce(() => Server.NotifyAll(text));
	}

	private (Result<ServerOutcome> Result, int Sent) Announce(Func<int> send)
	{
		return Require(UserPermissions.ChatManageAnnounce) is { } missing
			? (Result<ServerOutcome>.Denied(missing), 0)
			: (ServerOutcome.Ok, send());
	}

	/// <summary>Checks whether the caller holds the permissions an action on this server needs.</summary>
	/// <param name="required">The permissions the action needs.</param>
	/// <returns><see langword="null" /> when all are in effect; otherwise the permissions that are missing.</returns>
	internal UserPermissions? Needs(UserPermissions required)
	{
		return Require(required);
	}
}