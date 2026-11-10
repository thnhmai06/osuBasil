using System.Collections.Concurrent;
using System.Collections.Immutable;
using Basil.Application.Authentication.Persistence;
using Basil.Application.Common;
using Basil.Application.Users.Objects.Connections;
using Basil.Application.Users.Registry;
using Basil.Domain.Authentication;
using Basil.Domain.Common;
using Basil.Domain.Moderation;
using Basil.Domain.Users;

namespace Basil.Application.Users.Objects;

/// <summary>Owns the live connections of one online account.</summary>
public sealed class UserSessions
{
	private const int MaxLabelLength = 64;

	private static readonly SessionType[] ClientTypes =
		[SessionType.Stable, SessionType.StableTourney, SessionType.Irc, SessionType.Api];

	private readonly ConcurrentDictionary<SessionType, ConcurrentSet<Connection>> _connections = new();
	private readonly User _user;

	/// <summary>Creates the live connection state for an account.</summary>
	/// <param name="user">The account whose connections are held.</param>
	internal UserSessions(User user)
	{
		_user = user;
	}

	/// <summary>Gets the open connections of one kind.</summary>
	/// <param name="type">The kind of connection.</param>
	public IReadOnlySet<Connection> this[SessionType type] =>
		_connections.TryGetValue(type, out var set) ? set : ImmutableHashSet<Connection>.Empty;

	/// <summary>Gets every open connection of the user.</summary>
	public IEnumerable<Connection> Connections => _connections.SelectMany(pair => pair.Value);

	/// <summary>Gets the user's osu! game client connection, if any.</summary>
	/// <remarks>BasilBot's server-owned connection is not a game client, so the bot has none.</remarks>
	public StableConnection? Osu =>
		(StableConnection?)this[SessionType.Stable].SingleOrDefault(connection => !connection.IsServerOwned);

	/// <summary>Gets one of the user's IRC connections, if any.</summary>
	public IrcConnection? Irc => Ircs.FirstOrDefault();

	/// <summary>Gets the user's IRC connections.</summary>
	public IEnumerable<IrcConnection> Ircs => this[SessionType.Irc].Cast<IrcConnection>();

	/// <summary>Gets the user's osu!tourney connections.</summary>
	public IEnumerable<TourneyConnection> Tourneys => this[SessionType.StableTourney].Cast<TourneyConnection>();

	/// <summary>Adds a connection to the account.</summary>
	/// <param name="connection">The connection to add.</param>
	internal void Add(Connection connection)
	{
		using var scope = _user.Enter();
		var set = _connections.GetOrAdd(connection.Type, _ => []);
		set.Add(connection);
	}

	/// <summary>Removes a connection from the account.</summary>
	/// <param name="connection">The connection to remove.</param>
	/// <returns><see langword="true" /> if the connection was held and has been removed.</returns>
	internal bool Remove(Connection connection)
	{
		using var scope = _user.Enter();
		return _connections.TryGetValue(connection.Type, out var set) && set.Remove(connection);
	}

	/// <summary>Reads the sessions of this account.</summary>
	/// <param name="viewer">The connection reading the sessions.</param>
	/// <param name="page">Which part of the history to return.</param>
	/// <param name="state">Whether to include active sessions, ended sessions, or both.</param>
	/// <param name="types">The kinds of session to include, or <see langword="null" /> for every kind.</param>
	/// <param name="ct">A token that cancels the operation.</param>
	/// <returns>One part of the sessions, newest first.</returns>
	/// <remarks>Reading another account's sessions is recorded in the moderation log.</remarks>
	internal async Task<Page<SessionRecord>> ListAsync(Connection viewer, PageRequest page, SessionState state,
		IReadOnlyList<SessionType>? types, CancellationToken ct)
	{
		var now = _user.Context.Clock.GetUtcNow();
		if (!viewer.Record.Equals(_user.Record))
			await _user.Context.ModerationLog.AppendAsync(new ModerationData
			{
				Action = ModerationAction.ViewLogins,
				Actor = viewer.Record,
				Target = _user.Record,
				Timestamp = now
			});

		return await _user.Context.Sessions.ListAsync(new SessionQuery(_user.Record, state, types, now), page,
			ct);
	}

	/// <summary>Reads one session of this account.</summary>
	/// <param name="viewer">The connection reading the session.</param>
	/// <param name="id">The session's public identifier.</param>
	/// <param name="ct">A token that cancels the operation.</param>
	/// <returns>The outcome and, when successful, the session.</returns>
	/// <remarks>
	///     Reading another account's session is recorded in the moderation log. A session that does not belong to this
	///     account is not found.
	/// </remarks>
	internal async Task<(UserOutcome Outcome, SessionRecord? Session)> GetAsync(Connection viewer, long id,
		CancellationToken ct)
	{
		var session = await _user.Context.Sessions.GetAsync(_user.Record, id, ct);
		if (session is null) return (UserOutcome.NotFound, null);

		if (!viewer.Record.Equals(_user.Record))
			await _user.Context.ModerationLog.AppendAsync(new ModerationData
			{
				Action = ModerationAction.ViewLogins,
				Actor = viewer.Record,
				Target = _user.Record,
				Timestamp = _user.Context.Clock.GetUtcNow()
			});

		return (UserOutcome.Ok, session);
	}

	/// <summary>Reads the connections of one session of this account.</summary>
	/// <param name="viewer">The connection reading the connections.</param>
	/// <param name="id">The session's public identifier.</param>
	/// <param name="page">Which part of the trail to return.</param>
	/// <param name="ct">A token that cancels the operation.</param>
	/// <returns>The outcome and, when successful, one part of the connections, newest first.</returns>
	/// <remarks>
	///     Reading another account's connections is recorded in the moderation log. A session that does not belong to
	///     this account is not found.
	/// </remarks>
	internal async Task<(UserOutcome Outcome, Page<ConnectionRecord>? Connections)> ListConnectionsAsync(
		Connection viewer, long id, PageRequest page, CancellationToken ct)
	{
		var (outcome, _) = await GetAsync(viewer, id, ct);
		if (outcome is not UserOutcome.Ok) return (outcome, null);

		return (UserOutcome.Ok, await _user.Context.Connections.ListAsync(id, page, ct));
	}

	/// <summary>Closes one session of this account, whether its client is online or not.</summary>
	/// <param name="id">The session's public identifier.</param>
	/// <returns>The outcome of closing the session.</returns>
	/// <remarks>
	///     A closed session cannot sign in again with what it stored: an API token and an IRC key stop working. An
	///     osu! client is disconnected but can sign in again with its password. A session that does not belong to this
	///     account is not found.
	/// </remarks>
	internal async Task<UserOutcome> CloseAsync(long id)
	{
		if (_user.Record.IsBasilBot()) return UserOutcome.SystemAccount;
		// A session whose client is online exists even before its record reaches the store.
		if (!Connections.Any(connection => connection.Session.Id == id) &&
		    await _user.Context.Sessions.GetAsync(_user.Record, id) is null)
			return UserOutcome.NotFound;

		await CloseAsync(id, null, SessionEndReason.Revoked);
		return UserOutcome.Ok;
	}

	/// <summary>Closes every session of this account, whether its client is online or not.</summary>
	/// <param name="actor">The connection asking to close the sessions.</param>
	/// <param name="keepCurrent">
	///     Whether the HTTP API session making this call stays open; ignored when acting on another account.
	/// </param>
	/// <returns>The outcome of closing the sessions.</returns>
	/// <remarks>Closing another account's sessions is recorded in the moderation log.</remarks>
	internal async Task<UserOutcome> CloseAllAsync(Connection actor, bool keepCurrent)
	{
		if (_user.Record.IsBasilBot()) return UserOutcome.SystemAccount;

		var isSelf = actor.Record.Equals(_user.Record);
		long? keep = isSelf && keepCurrent && actor is ApiConnection api ? api.Session.Id : null;
		// The sessions live in another module's database, so no transaction covers both: log first, so an action is never unrecorded.
		if (!isSelf)
			await _user.Context.ModerationLog.AppendAsync(new ModerationData
			{
				Action = ModerationAction.CloseSessions,
				Actor = actor.Record,
				Target = _user.Record,
				Timestamp = _user.Context.Clock.GetUtcNow()
			});

		await CloseAsync(null, keep, SessionEndReason.Revoked);

		return UserOutcome.Ok;
	}

	/// <summary>Opens a long-lived IRC key for one IRC client of this account.</summary>
	/// <param name="actor">The connection asking for the key.</param>
	/// <param name="label">A name for the client the key is for, such as the machine it runs on.</param>
	/// <returns>The outcome and, when successful, the new key and its session.</returns>
	/// <remarks>
	///     The key is shown only here; closing the key's session stops it working and leaves the account's other IRC
	///     clients alone.
	/// </remarks>
	internal async Task<(UserOutcome Outcome, string? Key, SessionRecord? Session)> OpenIrcAsync(Connection actor,
		string label)
	{
		if (_user.Record.Value.DeletedAt is not null) return (UserOutcome.Deleted, null, null);

		label = label.Trim();
		if (label.Length is 0 or > MaxLabelLength) return (UserOutcome.InvalidLabel, null, null);

		var key = UserRegistry.NewToken();
		var now = _user.Context.Clock.GetUtcNow();
		var session = new SessionRecord
		{
			Id = UserRegistry.NewSessionId(),
			Value = new SessionData(_user.Record, SessionType.Irc, now,
				new SessionFingerprint(actor.Session.Value.Fingerprint.Ip, ClientInfo: label),
				UserRegistry.HashToken(key), null, now)
		};
		await _user.Context.Sessions.CreateOrUpdateAsync(session);
		return (UserOutcome.Ok, key, session);
	}

	/// <summary>Closes sessions of this account for good: its open connections, then every stored session.</summary>
	/// <param name="only">The one session to close, or <see langword="null" /> for every session.</param>
	/// <param name="except">A session that stays open, or <see langword="null" /> for none.</param>
	/// <param name="reason">Why the sessions close.</param>
	internal async Task CloseAsync(long? only, long? except, SessionEndReason reason)
	{
		_user.Context.Registry.CloseAll(_user.Record, reason, except, only);
		await _user.Context.Sessions.EndAsync(_user.Record, only, except, ClientTypes,
			_user.Context.Clock.GetUtcNow(), reason);
	}
}