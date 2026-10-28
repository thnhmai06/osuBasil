using System.Security.Cryptography;
using Basil.Application.Auth.Persistence;
using Basil.Application.Common;
using Basil.Application.Users.Connections;
using Basil.Domain.Auth;
using Basil.Domain.Moderation;
using Basil.Domain.Users;

namespace Basil.Application.Users.Users;

/// <summary>Views, opens and closes the sessions belonging to an account.</summary>
public sealed class ScopedUserSessions(ScopedUser scoped)
{
	private const int MaxLabelLength = 64;

	private static readonly ConnectionType[] ClientTypes =
		[ConnectionType.Osu, ConnectionType.Tourney, ConnectionType.Irc, ConnectionType.Api];

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
	public async Task<(Result<UserOutcome> Result, Page<SessionEntry>? Sessions)> ListAsync(PageRequest page,
		SessionState state = SessionState.Any, IReadOnlyList<ConnectionType>? types = null,
		CancellationToken cancellationToken = default)
	{
		if (!scoped.IsSelf && scoped.Guard(UserPermissions.UserManageViewSessions, false) is { } denied)
			return (denied, null);

		var now = scoped.User.Context.Clock.GetUtcNow();
		if (!scoped.IsSelf)
			await scoped.User.Context.ModerationLog.AppendAsync(new ModerationData
			{
				Action = ModerationAction.ViewLogins,
				By = scoped.By.Record,
				Target = scoped.User.Record,
				Timestamp = now
			}, cancellationToken);

		var sessions = await scoped.User.Context.Sessions.ListAsync(scoped.User.Record,
			new SessionFilter(state, types, now), page, cancellationToken);
		return (UserOutcome.Ok, sessions);
	}

	/// <summary>Closes one session of this account, whether its client is online or not.</summary>
	/// <param name="id">The session's public identifier.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The outcome of closing the session.</returns>
	/// <remarks>
	///     A closed session cannot sign in again with what it stored: an API token and an IRC key stop working, and an
	///     osu! device is asked once for its password. Closing another account's session needs
	///     <see cref="UserPermissions.UserManage" /> and hierarchy.
	/// </remarks>
	public async Task<Result<UserOutcome>> CloseAsync(Guid id, CancellationToken cancellationToken = default)
	{
		if (scoped.User.Record.IsBasilBot()) return UserOutcome.SystemAccount;
		if (!scoped.IsSelf && scoped.Guard(UserPermissions.UserManage, false) is { } denied) return denied;
		if (await scoped.User.Context.Sessions.GetAsync(scoped.User.Record, id, cancellationToken) is null)
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

		Guid? keep = scoped.IsSelf && keepCurrent && scoped.By is ApiConnection api ? api.Session.Id : null;
		await Close(null, keep, cancellationToken);
		if (!scoped.IsSelf)
			await scoped.User.Context.ModerationLog.AppendAsync(new ModerationData
			{
				Action = ModerationAction.CloseSessions,
				By = scoped.By.Record,
				Target = scoped.User.Record,
				Timestamp = scoped.User.Context.Clock.GetUtcNow()
			}, cancellationToken);

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
	public async Task<(Result<UserOutcome> Result, string? Key, SessionEntry? Session)> OpenIrcAsync(string label,
		CancellationToken cancellationToken = default)
	{
		if (scoped.User.Record.IsBasilBot()) return (UserOutcome.SystemAccount, null, null);
		if (!scoped.IsSelf) return (UserOutcome.NotSelf, null, null);
		if (scoped.User.Record.Value.DeletedAt is not null) return (UserOutcome.Deleted, null, null);

		label = label.Trim();
		if (label.Length is 0 or > MaxLabelLength) return (UserOutcome.InvalidLabel, null, null);

		var key = UserRegistry.NewToken();
		var session = new Session
		{
			Id = Guid.NewGuid(),
			User = scoped.User.Record,
			Type = ConnectionType.Irc,
			Ip = scoped.By.Session.Ip,
			ClientInfo = label,
			StartedAt = scoped.User.Context.Clock.GetUtcNow(),
			TokenHash = UserRegistry.HashToken(key)
		};
		await scoped.User.Context.Sessions.RecordAsync(session, cancellationToken);
		var entry = new SessionEntry(session.Id, session.User, session.Type, session.Ip, session.ClientInfo,
			session.StartedAt, session.LastActiveAt, null, null, false);
		return (UserOutcome.Ok, key, entry);
	}

	/// <summary>Closes sessions of this account for good: its open connections, then every stored session.</summary>
	private Task Close(Guid? only, Guid? except, CancellationToken cancellationToken)
	{
		return CloseCore(scoped.User, only, except, SessionEndReason.Revoked, true, cancellationToken);
	}

	/// <summary>Closes sessions of an account.</summary>
	/// <param name="user">The account whose sessions close.</param>
	/// <param name="only">The one session to close, or <see langword="null" /> for every session.</param>
	/// <param name="except">A session that stays open, or <see langword="null" /> for none.</param>
	/// <param name="reason">Why the sessions close.</param>
	/// <param name="requireReauthentication">Whether the devices of the closed osu! clients must enter their password again.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	internal static async Task CloseCore(User user, Guid? only, Guid? except, SessionEndReason reason,
		bool requireReauthentication, CancellationToken cancellationToken)
	{
		user.Context.Registry.CloseAll(user.Record, reason, except, requireReauthentication, only);
		await user.Context.Sessions.EndAsync(user.Record, only, except, ClientTypes,
			user.Context.Clock.GetUtcNow(), reason, requireReauthentication, cancellationToken);
	}
}
