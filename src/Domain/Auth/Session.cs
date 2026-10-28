using System.Net;
using Basil.Domain.Bancho;
using Basil.Domain.Users;

namespace Basil.Domain.Auth;

/// <summary>A persistent record of one client session.</summary>
public sealed class Session
{
	private DateTimeOffset? _lastActiveAt;
	private DateTimeOffset? _expiresAt;

	/// <summary>Gets the public identifier of this session.</summary>
	public required Guid Id { get; init; }

	/// <summary>Gets the user who logged in.</summary>
	public required UserRecord User { get; init; }

	/// <summary>Gets the kind of client that opened this session.</summary>
	public required ConnectionType Type { get; init; }

	/// <summary>Gets the IP address the session came from.</summary>
	public required IPAddress Ip { get; init; }

	/// <summary>Gets the osu! client that logged in, or <see langword="null" /> for a login without an osu! client.</summary>
	public BanchoClient? Client { get; init; }

	/// <summary>Gets client information reported for this session, when available.</summary>
	public string? ClientInfo { get; init; }

	/// <summary>Gets the date and time the session started.</summary>
	public required DateTimeOffset StartedAt { get; init; }

	/// <summary>Gets when the session was last active.</summary>
	public DateTimeOffset LastActiveAt => _lastActiveAt ?? StartedAt;

	/// <summary>Gets when the session ended, or <see langword="null" /> while it remains active.</summary>
	public DateTimeOffset? EndedAt { get; private set; }

	/// <summary>Gets why the session ended, or <see langword="null" /> while it remains active.</summary>
	public SessionEndReason? EndReason { get; private set; }

	/// <summary>Gets the hash of an API token or IRC key associated with the session.</summary>
	public byte[]? TokenHash { get; init; }

	/// <summary>Gets when the associated token expires, or <see langword="null" /> for a non-expiring session.</summary>
	public DateTimeOffset? ExpiresAt
	{
		get => _expiresAt;
		init => _expiresAt = value;
	}

	/// <summary>Gets whether the osu! device must enter its password again.</summary>
	public bool MustReauthenticate { get; private set; }

	/// <summary>Records activity on this session.</summary>
	/// <param name="at">The time of the activity.</param>
	public void MarkActive(DateTimeOffset at)
	{
		if (EndedAt is null && at >= LastActiveAt) _lastActiveAt = at;
	}

	/// <summary>Extends the sliding expiry of an API token session.</summary>
	/// <param name="at">The time of the authenticated activity.</param>
	/// <param name="lifetime">How long the token remains valid after activity.</param>
	public void ExtendExpiry(DateTimeOffset at, TimeSpan lifetime)
	{
		if (Type is ConnectionType.Api && EndedAt is null) _expiresAt = at + lifetime;
	}

	/// <summary>Ends this session at a moment for a specific reason.</summary>
	/// <param name="at">The time the session ended.</param>
	/// <param name="reason">Why the session ended.</param>
	public void End(DateTimeOffset at, SessionEndReason reason)
	{
		if (EndedAt is not null) return;

		EndedAt = at;
		EndReason = reason;
	}

	/// <summary>Requires the osu! device to authenticate with its password on the next login.</summary>
	public void RequireReauthentication()
	{
		MustReauthenticate = true;
	}
}
