using System.Net;
using Basil.Application.Common;
using Basil.Domain.Authentication;

namespace Basil.Application.Authentication.Persistence;

/// <summary>The latest activity of the open connection of one session.</summary>
/// <param name="SessionId">The session's public identifier.</param>
/// <param name="LastActiveAt">When the connection was last used.</param>
public sealed record ConnectionActivity(long SessionId, DateTimeOffset LastActiveAt);

/// <summary>Stores the trail of connections of sessions: when each client connected, from where, and how it ended.</summary>
/// <remarks>
///     A session holds at most one open connection. A trail lives and dies with its session: deleting a session deletes
///     its connections.
/// </remarks>
public interface IConnectionRepository
{
	/// <summary>Records that a client connected to a session.</summary>
	/// <param name="sessionId">The session the client connected to.</param>
	/// <param name="ip">The IP address the client connected from.</param>
	/// <param name="clientInfo">Client information reported for the connection, when available.</param>
	/// <param name="at">The moment the connection opened.</param>
	/// <param name="reopenWithin">
	///     How long after the session's last connection closed a connection from the same address is counted as the
	///     same one coming back.
	/// </param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <remarks>
	///     A connection from the same address that comes back within <paramref name="reopenWithin" /> reopens the
	///     session's last connection instead of adding another.
	/// </remarks>
	Task OpenAsync(long sessionId, IPAddress ip, string? clientInfo, DateTimeOffset at, TimeSpan reopenWithin,
		CancellationToken cancellationToken = default);

	/// <summary>Records that the open connection of a session closed.</summary>
	/// <param name="sessionId">The session the connection belonged to.</param>
	/// <param name="lastActiveAt">The most recent activity of the connection.</param>
	/// <param name="closedAt">The moment the connection closed.</param>
	/// <param name="reason">Why the connection closed.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	Task CloseAsync(long sessionId, DateTimeOffset lastActiveAt, DateTimeOffset closedAt, SessionEndReason reason,
		CancellationToken cancellationToken = default);

	/// <summary>Stores the latest activity of several open connections at once.</summary>
	/// <param name="activity">The activity to store.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	Task WriteActivityAsync(IReadOnlyCollection<ConnectionActivity> activity,
		CancellationToken cancellationToken = default);

	/// <summary>Lists the connections of a session, newest first.</summary>
	/// <param name="sessionId">The session whose connections to list.</param>
	/// <param name="page">Which part of the listing to return.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>One part of the session's connections, newest first.</returns>
	ValueTask<Page<ConnectionRecord>> ListAsync(long sessionId, PageRequest page,
		CancellationToken cancellationToken = default);

	/// <summary>Closes every connection still open, at the moment it was last active.</summary>
	/// <param name="reason">Why the connections closed.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>How many connections were closed.</returns>
	Task<int> CloseOpenAsync(SessionEndReason reason, CancellationToken cancellationToken = default);
}
