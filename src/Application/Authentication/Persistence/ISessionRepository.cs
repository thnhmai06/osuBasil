using System.Net;
using Basil.Application.Shared;
using Basil.Domain.Authentication;
using Basil.Domain.Cryptography;
using Basil.Domain.Users;

namespace Basil.Application.Authentication.Persistence;

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
/// <param name="User">The user whose sessions to list.</param>
/// <param name="State">Whether to include active sessions, ended sessions, or both.</param>
/// <param name="Types">The kinds of session to include, or <see langword="null" /> for every kind.</param>
/// <param name="Now">The moment that decides whether a session has expired.</param>
public sealed record SessionQuery(UserRecord User, SessionState State, IReadOnlyList<SessionType>? Types,
	DateTimeOffset Now);

/// <summary>A session found by the token or key that opens it.</summary>
/// <param name="Id">The session's public identifier.</param>
/// <param name="UserId">The user the session belongs to.</param>
/// <param name="Type">The kind of client the session is for.</param>
/// <param name="Ip">The address the session was opened from.</param>
/// <param name="ClientInfo">The client details or label of the session.</param>
/// <param name="StartedAt">When the session started.</param>
/// <param name="ExpiresAt">When the token expires, or <see langword="null" /> if it never does.</param>
public sealed record StoredSession(
	long Id,
	int UserId,
	SessionType Type,
	IPAddress Ip,
	string? ClientInfo,
	DateTimeOffset StartedAt,
	DateTimeOffset? ExpiresAt);

/// <summary>The latest activity of one session.</summary>
/// <param name="Id">The session's public identifier.</param>
/// <param name="LastActiveAt">When the session was last used.</param>
/// <param name="ExpiresAt">The new expiry of the session's token, or <see langword="null" /> if it has none.</param>
public sealed record SessionActivity(long Id, DateTimeOffset LastActiveAt, DateTimeOffset? ExpiresAt);

/// <summary>Stores the sessions of users: their history, and the tokens and keys that open them.</summary>
/// <remarks>
///     The history is kept for audit; staff read it back, and each read is itself logged to the moderation log by the
///     caller. Tokens and keys are stored only as hashes.
/// </remarks>
public interface ISessionRepository
{
	/// <summary>Stores a session: records it when it is new, and replaces its stored state when it is already there.</summary>
	/// <param name="session">The session as it stands now, with its activity and end.</param>
	Task CreateOrUpdateAsync(SessionRecord session);

	/// <summary>Lists the sessions of a user, newest first.</summary>
	/// <param name="query">Whose sessions to list and which of them to include.</param>
	/// <param name="page">Which part of the listing to return.</param>
	/// <param name="ct">A token that cancels the operation.</param>
	/// <returns>One part of the user's sessions, newest first, with how many sessions match in total.</returns>
	ValueTask<Page<SessionRecord>> ListAsync(SessionQuery query, PageRequest page,
		CancellationToken ct = default);

	/// <summary>Gets one session of a user.</summary>
	/// <param name="user">The user the session must belong to.</param>
	/// <param name="id">The session's public identifier.</param>
	/// <param name="ct">A token that cancels the operation.</param>
	/// <returns>The session, or <see langword="null" /> when the user has no session with that id.</returns>
	Task<SessionRecord?> GetAsync(UserRecord user, long id, CancellationToken ct = default);

	/// <summary>Finds the session a token or key opens.</summary>
	/// <param name="type">The kind of session the token or key is for.</param>
	/// <param name="tokenHash">The SHA-256 hash of the token or key.</param>
	/// <param name="now">The moment that decides whether the token has expired.</param>
	/// <param name="ct">A token that cancels the operation.</param>
	/// <returns>The session, or <see langword="null" /> when none is active and unexpired with that hash.</returns>
	Task<StoredSession?> FindByTokenAsync(SessionType type, Sha256 tokenHash, DateTimeOffset now,
		CancellationToken ct = default);

	/// <summary>Ends the unended sessions of a user that match a selection.</summary>
	/// <param name="user">The user whose sessions end.</param>
	/// <param name="only">The one session to end, or <see langword="null" /> to end every matching session.</param>
	/// <param name="except">A session to keep, or <see langword="null" /> to keep none.</param>
	/// <param name="types">The kinds of session that end.</param>
	/// <param name="at">When the sessions end.</param>
	/// <param name="reason">Why the sessions end.</param>
	Task EndAsync(UserRecord user, long? only, long? except, IReadOnlyList<SessionType> types, DateTimeOffset at,
		SessionEndReason reason);

	/// <summary>Ends the unended sessions of some kinds, each at the moment it was last active.</summary>
	/// <param name="types">The kinds of session that end.</param>
	/// <param name="reason">Why the sessions end.</param>
	/// <returns>How many sessions ended.</returns>
	Task<int> EndOpenAsync(IReadOnlyList<SessionType> types, SessionEndReason reason);

	/// <summary>Stores the latest activity of several sessions at once.</summary>
	/// <param name="activity">The activity to store.</param>
	Task WriteActivityAsync(IReadOnlyCollection<SessionActivity> activity);

	/// <summary>Deletes the sessions that ended or expired before a moment, which are no longer worth keeping.</summary>
	/// <param name="cutoff">The moment before which a session must have ended to be deleted.</param>
	/// <returns>How many sessions were deleted.</returns>
	Task<int> DeleteEndedBeforeAsync(DateTimeOffset cutoff);
}