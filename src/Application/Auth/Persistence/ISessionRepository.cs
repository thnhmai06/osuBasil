using System.Net;
using Basil.Application.Common;
using Basil.Domain.Auth;
using Basil.Domain.Users;

namespace Basil.Application.Auth.Persistence;

/// <summary>Which sessions of a user a listing includes.</summary>
public enum SessionState : byte
{
	/// <summary>Every session, ended or not.</summary>
	Any,

	/// <summary>Sessions that have not ended and have not expired.</summary>
	Active,

	/// <summary>Sessions that have ended or expired.</summary>
	Ended
}

/// <summary>Selects the sessions of a user a listing includes.</summary>
/// <param name="State">Whether to include active sessions, ended sessions, or both.</param>
/// <param name="Types">The kinds of session to include, or <see langword="null" /> for every kind.</param>
/// <param name="Now">The moment that decides whether a session has expired.</param>
public sealed record SessionFilter(SessionState State, IReadOnlyList<ConnectionType>? Types, DateTimeOffset Now);

/// <summary>A session found by the token or key that opens it.</summary>
/// <param name="Id">The session's public identifier.</param>
/// <param name="UserId">The user the session belongs to.</param>
/// <param name="Type">The kind of client the session is for.</param>
/// <param name="Ip">The address the session was opened from.</param>
/// <param name="ClientInfo">The client details or label of the session.</param>
/// <param name="StartedAt">When the session started.</param>
/// <param name="ExpiresAt">When the token expires, or <see langword="null" /> if it never does.</param>
public sealed record StoredSession(Guid Id, int UserId, ConnectionType Type, IPAddress Ip, string? ClientInfo,
	DateTimeOffset StartedAt, DateTimeOffset? ExpiresAt);

/// <summary>The latest activity of one session.</summary>
/// <param name="Id">The session's public identifier.</param>
/// <param name="LastActiveAt">When the session was last used.</param>
/// <param name="ExpiresAt">The new expiry of the session's token, or <see langword="null" /> if it has none.</param>
public sealed record SessionActivity(Guid Id, DateTimeOffset LastActiveAt, DateTimeOffset? ExpiresAt);

/// <summary>Stores the sessions of users: their history, and the tokens and keys that open them.</summary>
/// <remarks>
///     The history is kept for audit; staff read it back, and each read is itself logged to the moderation log by the
///     caller. Tokens and keys are stored only as hashes.
/// </remarks>
public interface ISessionRepository
{
	/// <summary>Records a new session.</summary>
	/// <param name="login">The session to record.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	Task RecordAsync(Session login, CancellationToken cancellationToken = default);

	/// <summary>Updates the stored state of a session.</summary>
	/// <param name="session">The session state to store.</param>
	Task UpdateAsync(Session session);

	/// <summary>Lists the sessions of a user, newest first.</summary>
	/// <param name="user">The user whose sessions to list.</param>
	/// <param name="filter">Which sessions to include.</param>
	/// <param name="page">Which part of the listing to return.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>One part of the user's sessions, newest first, with how many sessions match in total.</returns>
	ValueTask<Page<SessionEntry>> ListAsync(UserRecord user, SessionFilter filter, PageRequest page,
		CancellationToken cancellationToken = default);

	/// <summary>Gets one session of a user.</summary>
	/// <param name="user">The user the session must belong to.</param>
	/// <param name="id">The session's public identifier.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The session, or <see langword="null" /> when the user has no session with that id.</returns>
	Task<SessionEntry?> GetAsync(UserRecord user, Guid id, CancellationToken cancellationToken = default);

	/// <summary>Finds the session a token or key opens.</summary>
	/// <param name="type">The kind of session the token or key is for.</param>
	/// <param name="tokenHash">The SHA-256 hash of the token or key.</param>
	/// <param name="now">The moment that decides whether the token has expired.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The session, or <see langword="null" /> when none is active and unexpired with that hash.</returns>
	Task<StoredSession?> FindByTokenAsync(ConnectionType type, byte[] tokenHash, DateTimeOffset now,
		CancellationToken cancellationToken = default);

	/// <summary>Ends the unended sessions of a user that match a selection.</summary>
	/// <param name="user">The user whose sessions end.</param>
	/// <param name="only">The one session to end, or <see langword="null" /> to end every matching session.</param>
	/// <param name="except">A session to keep, or <see langword="null" /> to keep none.</param>
	/// <param name="types">The kinds of session that end.</param>
	/// <param name="at">When the sessions end.</param>
	/// <param name="reason">Why the sessions end.</param>
	/// <param name="requireReauthentication">
	///     Whether the devices of the ended osu! and osu!tourney sessions must enter their password again.
	/// </param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	Task EndAsync(UserRecord user, Guid? only, Guid? except, IReadOnlyList<ConnectionType> types, DateTimeOffset at,
		SessionEndReason reason, bool requireReauthentication, CancellationToken cancellationToken = default);

	/// <summary>Stores the latest activity of several sessions at once.</summary>
	/// <param name="activity">The activity to store.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	Task WriteActivityAsync(IReadOnlyCollection<SessionActivity> activity,
		CancellationToken cancellationToken = default);

	/// <summary>Takes the one-time mark that makes an osu! device enter its password again.</summary>
	/// <param name="user">The user logging in.</param>
	/// <param name="device">The hashes that identify the device.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns><see langword="true" /> when the device was marked; the mark is cleared so only one login is refused.</returns>
	Task<bool> TakeReauthenticationAsync(UserRecord user, DeviceId device,
		CancellationToken cancellationToken = default);
}

/// <summary>The hashes by which an osu! device is recognised.</summary>
/// <param name="UninstallHash">The uninstall id hash the client reported.</param>
/// <param name="DiskSignatureHash">The disk signature hash the client reported.</param>
public sealed record DeviceId(string UninstallHash, string DiskSignatureHash);
