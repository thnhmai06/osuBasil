using Basil.Application.Authentication.Persistence;
using Basil.Application.Common;
using Basil.Application.Users.Objects;
using Basil.Domain.Authentication;
using Basil.Domain.Users;

namespace Basil.Application.Users.Capabilities;

/// <summary>Views, opens and closes the sessions belonging to an account.</summary>
public readonly struct ScopedUserSessions(ScopedUser scoped)
{
	/// <summary>Reads the sessions of this account.</summary>
	/// <param name="page">Which part of the history to return.</param>
	/// <param name="state">Whether to include active sessions, ended sessions, or both.</param>
	/// <param name="types">The kinds of session to include, or <see langword="null" /> for every kind.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The outcome and, when successful, one part of the sessions, newest first.</returns>
	/// <remarks>
	///     A user may read their own sessions. Reading another account's sessions needs
	///     <see cref="UserPrivileges.Moderator" /> and is recorded in the moderation log.
	/// </remarks>
	public async Task<(Result<UserOutcome> Result, Page<SessionRecord>? Sessions)> ListAsync(PageRequest page,
		SessionState state = SessionState.Any, IReadOnlyList<SessionType>? types = null,
		CancellationToken cancellationToken = default)
	{
		if (!scoped.IsSelf && scoped.Guard(UserPrivileges.Moderator, false) is { } denied)
			return (denied, null);

		var sessions = await scoped.User.Sessions.ListAsync(scoped.Actor, page, state, types, cancellationToken);
		return (UserOutcome.Ok, sessions);
	}

	/// <summary>Reads one session of this account.</summary>
	/// <param name="id">The session's public identifier.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The outcome and, when successful, the session.</returns>
	/// <remarks>
	///     A user may read their own sessions. Reading another account's session needs
	///     <see cref="UserPrivileges.Moderator" /> and is recorded in the moderation log. A session that
	///     does not belong to this account is not found.
	/// </remarks>
	public async Task<(Result<UserOutcome> Result, SessionRecord? Session)> GetAsync(long id,
		CancellationToken cancellationToken = default)
	{
		if (!scoped.IsSelf && scoped.Guard(UserPrivileges.Moderator, false) is { } denied)
			return (denied, null);

		var (outcome, session) = await scoped.User.Sessions.GetAsync(scoped.Actor, id, cancellationToken);
		return (outcome, session);
	}

	/// <summary>Reads the connections of one session of this account: when and from where each client connected.</summary>
	/// <param name="id">The session's public identifier.</param>
	/// <param name="page">Which part of the trail to return.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The outcome and, when successful, one part of the connections, newest first.</returns>
	/// <remarks>
	///     A user may read the connections of their own sessions. Reading another account's needs
	///     <see cref="UserPrivileges.Moderator" /> and is recorded in the moderation log. A session that
	///     does not belong to this account is not found.
	/// </remarks>
	public async Task<(Result<UserOutcome> Result, Page<ConnectionRecord>? Connections)> ListConnectionsAsync(
		long id, PageRequest page, CancellationToken cancellationToken = default)
	{
		if (!scoped.IsSelf && scoped.Guard(UserPrivileges.Moderator, false) is { } denied)
			return (denied, null);

		var (outcome, connections) =
			await scoped.User.Sessions.ListConnectionsAsync(scoped.Actor, id, page, cancellationToken);
		return (outcome, connections);
	}

	/// <summary>Closes one session of this account, whether its client is online or not.</summary>
	/// <param name="id">The session's public identifier.</param>
	/// <returns>The outcome of closing the session.</returns>
	/// <remarks>
	///     A closed session cannot sign in again with what it stored: an API token and an IRC key stop working. An
	///     osu! client is disconnected but can sign in again with its password. Closing another account's session needs
	///     <see cref="UserPrivileges.Owner" /> and hierarchy.
	/// </remarks>
	public async Task<Result<UserOutcome>> CloseAsync(long id)
	{
		if (!scoped.IsSelf && scoped.Guard(UserPrivileges.Owner, false) is { } denied) return denied;
		return await scoped.User.Sessions.CloseAsync(id);
	}

	/// <summary>Closes every session of this account, whether its client is online or not.</summary>
	/// <param name="keepCurrent">
	///     Whether the HTTP API session making this call stays open; ignored when acting on another account.
	/// </param>
	/// <returns>The outcome of closing the sessions.</returns>
	/// <remarks>
	///     Closing another account's sessions needs <see cref="UserPrivileges.Owner" /> and hierarchy, and is
	///     recorded in the moderation log.
	/// </remarks>
	public async Task<Result<UserOutcome>> CloseAllAsync(bool keepCurrent = true)
	{
		if (!scoped.IsSelf && scoped.Guard(UserPrivileges.Owner, false) is { } denied) return denied;
		return await scoped.User.Sessions.CloseAllAsync(scoped.Actor, keepCurrent);
	}

	/// <summary>Opens a long-lived IRC key for one IRC client of this account.</summary>
	/// <param name="label">A name for the client the key is for, such as the machine it runs on.</param>
	/// <returns>The outcome and, when successful, the new key and its session.</returns>
	/// <remarks>
	///     Only the account's own user may open a key. The key is shown only here; closing the key's session stops it
	///     working and leaves the account's other IRC clients alone.
	/// </remarks>
	public async Task<(Result<UserOutcome> Result, string? Key, SessionRecord? Session)> OpenIrcAsync(string label)
	{
		if (scoped.User.Record.IsBasilBot()) return (UserOutcome.SystemAccount, null, null);
		if (!scoped.IsSelf) return (UserOutcome.NotSelf, null, null);
		var (outcome, key, session) = await scoped.User.Sessions.OpenIrcAsync(scoped.Actor, label);
		return (outcome, key, session);
	}
}