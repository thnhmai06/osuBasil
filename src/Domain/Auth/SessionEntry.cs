using System.Net;
using Basil.Domain.Users;

namespace Basil.Domain.Auth;

/// <summary>A stored view of one client session.</summary>
/// <param name="Id">The session's public identifier.</param>
/// <param name="User">The user who logged in.</param>
/// <param name="Type">The kind of client that opened the session.</param>
/// <param name="Ip">The address the user logged in from.</param>
/// <param name="ClientInfo">Client details reported for the session, when available.</param>
/// <param name="StartedAt">The moment the session started.</param>
/// <param name="LastActiveAt">The most recent activity recorded for the session.</param>
/// <param name="EndedAt">When the session ended, or <see langword="null" /> while active.</param>
/// <param name="EndReason">Why the session ended, or <see langword="null" /> while active.</param>
/// <param name="MustReauthenticate">Whether the osu! device must enter its password again.</param>
public sealed record SessionEntry(Guid Id, UserRecord User, SessionType Type, IPAddress Ip, string? ClientInfo,
	DateTimeOffset StartedAt, DateTimeOffset LastActiveAt, DateTimeOffset? EndedAt, SessionEndReason? EndReason,
	bool MustReauthenticate);
