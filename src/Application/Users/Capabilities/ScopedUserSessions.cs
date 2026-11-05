using Basil.Application.Authentication.Persistence;
using Basil.Application.Common;
using Basil.Application.Users.Objects;
using Basil.Application.Users.Objects.Connections;
using Basil.Application.Users.Registry;
using Basil.Domain.Authentication;
using Basil.Domain.Moderation;
using Basil.Domain.Users;

namespace Basil.Application.Users.Capabilities;

/// <summary>Views, opens and closes the sessions belonging to an account.</summary>
public readonly struct ScopedUserSessions(ScopedUser scoped)
{
	private const int MaxLabelLength = 64;

	private static readonly SessionType[] ClientTypes =
		[SessionType.Stable, SessionType.StableTourney, SessionType.Irc, SessionType.Api];

	/// <summary>Reads the sessions of this account.</summary>
	/// <param name="page">Which part of the history to return.</param>
	/// <param name="state">Whether to include active sessions, ended sessions, or both.</param>
	/// <param name="types">The kinds of session to include, or <see langword="null" /> for every kind.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The outcome and, when successful, one part of the sessions, newest first.</returns>
	/// <remarks>
	///     A user may read their own sessions. Reading another account's sessions needs
	///     <see cref="UserPermissions.UserManageViewSessions" /> and is recorded in the moderation log.
	/// </remarks>
	public async Task<(Result<UserOutcome> Result, Page<SessionRecord>? Sessions)> ListAsync(PageRequest page,
		SessionState state = SessionState.Any, IReadOnlyList<SessionType>? types = null,
		CancellationToken cancellationToken = default)
	{
		if (!scoped.IsSelf && scoped.Guard(UserPermissions.UserManageViewSessions, false) is { } denied)
			return (denied, null);

		var now = scoped.User.Context.Clock.GetUtcNow();
		if (!scoped.IsSelf)
			await scoped.User.Context.ModerationLog.AppendAsync(new ModerationData
			{
				Action = ModerationAction.ViewLogins,
				Actor = scoped.Actor.Record,
				Target = scoped.User.Record,
				Timestamp = now
			}, cancellationToken);

		var sessions = await scoped.User.Context.Sessions.ListAsync(scoped.User.Record,
			new SessionFilter(state, types, now), page, cancellationToken);
		return (UserOutcome.Ok, sessions);
	}

	/// <summary>Reads one session of this account.</summary>
	/// <param name="id">The session's public identifier.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The outcome and, when successful, the session.</returns>
	/// <remarks>
	///     A user may read their own sessions. Reading another account's session needs
	///     <see cref="UserPermissions.UserManageViewSessions" /> and is recorded in the moderation log. A session that
	///     does not belong to this account is not found.
	/// </remarks>
	public async Task<(Result<UserOutcome> Result, SessionRecord? Session)> GetAsync(Guid id,
		CancellationToken cancellationToken = default)
	{
		if (!scoped.IsSelf && scoped.Guard(UserPermissions.UserManageViewSessions, false) is { } denied)
			return (denied, null);

		var session = await scoped.User.Context.Sessions.GetAsync(scoped.User.Record, id, cancellationToken);
		if (session is null) return (UserOutcome.NotFound, null);

		if (!scoped.IsSelf)
			await scoped.User.Context.ModerationLog.AppendAsync(new ModerationData
			{
				Action = ModerationAction.ViewLogins,
				Actor = scoped.Actor.Record,
				Target = scoped.User.Record,
				Timestamp = scoped.User.Context.Clock.GetUtcNow()
			}, cancellationToken);

		return (UserOutcome.Ok, session);
	}

	/// <summary>Closes one session of this account, whether its client is online or not.</summary>
	/// <param name="id">The session's public identifier.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The outcome of closing the session.</returns>
	/// <remarks>
	///     A closed session cannot sign in again with what it stored: an API token and an IRC key stop working. An
	///     osu! client is disconnected but can sign in again with its password. Closing another account's session needs
	///     <see cref="UserPermissions.UserManage" /> and hierarchy.
	/// </remarks>
	public async Task<Result<UserOutcome>> CloseAsync(Guid id, CancellationToken cancellationToken = default)
	{
		if (scoped.User.Record.IsBasilBot()) return UserOutcome.SystemAccount;
		if (!scoped.IsSelf && scoped.Guard(UserPermissions.UserManage, false) is { } denied) return denied;
		// A session whose client is online exists even before its record reaches the store.
		if (!scoped.User.Connections.Any(connection => connection.Session.Id == id) &&
		    await scoped.User.Context.Sessions.GetAsync(scoped.User.Record, id, cancellationToken) is null)
			return UserOutcome.NotFound;

		await Close(id, null, cancellationToken);
		return UserOutcome.Ok;
	}

	/// <summary>Closes every session of this account, whether its client is online or not.</summary>
	/// <param name="keepCurrent">
	///     Whether the HTTP API session making this call stays open; ignored when acting on another account.
	/// </param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The outcome of closing the sessions.</returns>
	/// <remarks>
	///     Closing another account's sessions needs <see cref="UserPermissions.UserManage" /> and hierarchy, and is
	///     recorded in the moderation log.
	/// </remarks>
	public async Task<Result<UserOutcome>> CloseAllAsync(bool keepCurrent = true,
		CancellationToken cancellationToken = default)
	{
		if (scoped.User.Record.IsBasilBot()) return UserOutcome.SystemAccount;
		if (!scoped.IsSelf && scoped.Guard(UserPermissions.UserManage, false) is { } denied) return denied;

		Guid? keep = scoped.IsSelf && keepCurrent && scoped.Actor is ApiConnection api ? api.Session.Id : null;
		// The sessions live in another module's database, so no transaction covers both: log first, so an action is never unrecorded.
		if (!scoped.IsSelf)
			await scoped.User.Context.ModerationLog.AppendAsync(new ModerationData
			{
				Action = ModerationAction.CloseSessions,
				Actor = scoped.Actor.Record,
				Target = scoped.User.Record,
				Timestamp = scoped.User.Context.Clock.GetUtcNow()
			}, cancellationToken);

		await Close(null, keep, cancellationToken);

		return UserOutcome.Ok;
	}

	/// <summary>Opens a long-lived IRC key for one IRC client of this account.</summary>
	/// <param name="label">A name for the client the key is for, such as the machine it runs on.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The outcome and, when successful, the new key and its session.</returns>
	/// <remarks>
	///     Only the account's own user may open a key. The key is shown only here; closing the key's session stops it
	///     working and leaves the account's other IRC clients alone.
	/// </remarks>
	public async Task<(Result<UserOutcome> Result, string? Key, SessionRecord? Session)> OpenIrcAsync(string label,
		CancellationToken cancellationToken = default)
	{
		if (scoped.User.Record.IsBasilBot()) return (UserOutcome.SystemAccount, null, null);
		if (!scoped.IsSelf) return (UserOutcome.NotSelf, null, null);
		if (scoped.User.Record.Value.DeletedAt is not null) return (UserOutcome.Deleted, null, null);

		label = label.Trim();
		if (label.Length is 0 or > MaxLabelLength) return (UserOutcome.InvalidLabel, null, null);

		var key = UserRegistry.NewToken();
		var now = scoped.User.Context.Clock.GetUtcNow();
		var session = new SessionRecord(Guid.NewGuid(), scoped.User.Record, SessionType.Irc, now,
			new SessionData(new SessionDevice(scoped.Actor.Session.Value.Device.Ip, ClientInfo: label),
				new SessionCredential(UserRegistry.HashToken(key)), now));
		await scoped.User.Context.Sessions.RecordAsync(session, cancellationToken);
		return (UserOutcome.Ok, key, session);
	}

	/// <summary>Closes sessions of this account for good: its open connections, then every stored session.</summary>
	private Task Close(Guid? only, Guid? except, CancellationToken cancellationToken)
	{
		return CloseCore(scoped.User, only, except, SessionEndReason.Revoked, cancellationToken);
	}

	/// <summary>Closes sessions of an account.</summary>
	/// <param name="user">The account whose sessions close.</param>
	/// <param name="only">The one session to close, or <see langword="null" /> for every session.</param>
	/// <param name="except">A session that stays open, or <see langword="null" /> for none.</param>
	/// <param name="reason">Why the sessions close.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	internal static async Task CloseCore(User user, Guid? only, Guid? except, SessionEndReason reason,
		CancellationToken cancellationToken)
	{
		user.Context.Registry.CloseAll(user.Record, reason, except, only);
		await user.Context.Sessions.EndAsync(user.Record, only, except, ClientTypes,
			user.Context.Clock.GetUtcNow(), reason, cancellationToken);
	}
}