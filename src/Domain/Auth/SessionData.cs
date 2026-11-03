using System.Net;
using Basil.Domain.Client;

namespace Basil.Domain.Auth;

/// <summary>The data of a stored session, in the groups it is read and changed by.</summary>
/// <param name="Device">Where the session came from.</param>
/// <param name="Credential">The token or key that opens the session.</param>
/// <param name="End">How the session ended, or <see langword="null" /> while it remains active.</param>
public sealed record SessionData(SessionDevice Device, SessionCredential Credential, SessionEnd? End = null);

/// <summary>The device a session came from.</summary>
/// <param name="Ip">The IP address the session came from.</param>
/// <param name="Client">The osu! client that logged in, or <see langword="null" /> for a login without an osu! client.</param>
/// <param name="ClientInfo">Client information reported for the session, when available.</param>
public sealed record SessionDevice(IPAddress Ip, OsuClient? Client = null, string? ClientInfo = null);

/// <summary>The token or key associated with a session.</summary>
/// <param name="TokenHash">The hash of an API token or IRC key, or <see langword="null" /> for a session without one.</param>
/// <param name="ExpiresAt">When the token expires, or <see langword="null" /> for a non-expiring session.</param>
public sealed record SessionCredential(byte[]? TokenHash = null, DateTimeOffset? ExpiresAt = null);

/// <summary>How a session ended.</summary>
/// <param name="At">The moment the session ended.</param>
/// <param name="Reason">Why the session ended.</param>
public sealed record SessionEnd(DateTimeOffset At, SessionEndReason Reason);