using Basil.Application.Common;
using Basil.Application.Users.Capabilities;
using Basil.Application.Users.Objects.Events;
using Basil.Domain.Authentication;
using Basil.Domain.Client;
using Basil.Domain.Common;
using Basil.Domain.Users;

namespace Basil.Application.Users.Objects.Connections;

/// <summary>One login of a user from one client; two connections are never equal.</summary>
public abstract class Connection(SessionRecord login, string token) : IActor
{
	// UTC ticks, written and read atomically: a connection and the activity flush touch them from different threads.
	private const long None = long.MinValue;
	private SessionEndStatus? _end;
	private long _expiresTicks = login.Value.Credential.ExpiresAt?.UtcTicks ?? None;
	private long _lastActiveTicks = login.Value.LastActiveAt.UtcTicks;

	/// <summary>Gets the online user this connection belongs to.</summary>
	public User User { get; internal set; } = null!;

	/// <summary>Gets the stored session that opened this connection, as it was when the connection opened.</summary>
	public SessionRecord Session { get; } = login;

	/// <summary>Gets the opaque token that identifies this connection.</summary>
	public string Token { get; } = token;

	/// <summary>Gets the kind of connection.</summary>
	public abstract SessionType Type { get; }

	/// <summary>Gets a value that indicates whether the connection is still open.</summary>
	public bool IsOpen { get; internal set; }

	/// <summary>Gets when this connection was last active.</summary>
	public DateTimeOffset LastActiveAt => new(Interlocked.Read(ref _lastActiveTicks), TimeSpan.Zero);

	/// <summary>Gets when the session's token expires, or <see langword="null" /> for a non-expiring session.</summary>
	public DateTimeOffset? ExpiresAt =>
		Interlocked.Read(ref _expiresTicks) is var ticks && ticks != None
			? new DateTimeOffset(ticks, TimeSpan.Zero)
			: null;

	/// <summary>Gets how the session ended, or <see langword="null" /> while it remains active.</summary>
	public SessionEndStatus? End => Volatile.Read(ref _end);

	/// <summary>Gets a value that indicates whether the server opened this connection for BasilBot, with no client behind it.</summary>
	public bool IsServerOwned => Record.IsBasilBot();

	/// <summary>Gets the user who owns this connection.</summary>
	public UserRecord Record => Session.User;

	/// <inheritdoc />
	public virtual UserPermissions EffectivePermissions(DateTimeOffset now)
	{
		return User.EffectivePermissions(now);
	}

	/// <summary>Gets the session as it stands now, ready to be stored.</summary>
	/// <returns>The stored session with its current expiry, activity and end.</returns>
	public SessionRecord Snapshot()
	{
		var value = Session.Value;
		return new SessionRecord(Session.User, Session.TokenHash, Session.Type,
			Session.CreatedAt, value with
			{
				Credential = value.Credential with { ExpiresAt = ExpiresAt },
				LastActiveAt = LastActiveAt,
				End = End
			});
	}

	/// <summary>Records activity on this connection's session.</summary>
	/// <param name="at">The time of the activity.</param>
	internal void MarkActive(DateTimeOffset at)
	{
		if (End is null && at >= LastActiveAt) Interlocked.Exchange(ref _lastActiveTicks, at.UtcTicks);
	}

	/// <summary>Extends the sliding expiry of an API token session.</summary>
	/// <param name="at">The time of the authenticated activity.</param>
	/// <param name="lifetime">How long the token remains valid after activity.</param>
	internal void ExtendExpiry(DateTimeOffset at, TimeSpan lifetime)
	{
		if (Type is SessionType.Api && End is null) Interlocked.Exchange(ref _expiresTicks, (at + lifetime).UtcTicks);
	}

	/// <summary>Ends this connection's session at a moment for a specific reason; an ended session stays ended.</summary>
	/// <param name="at">The time the session ended.</param>
	/// <param name="reason">Why the session ended.</param>
	internal void EndSession(DateTimeOffset at, SessionEndReason reason)
	{
		Interlocked.CompareExchange(ref _end, new SessionEndStatus(at, reason), null);
	}

	/// <summary>Tells whether this connection's authority is above an account's permissions.</summary>
	/// <param name="granted">The permissions granted to this connection's user when the scope was made.</param>
	/// <param name="target">The permissions granted to the account acted on.</param>
	/// <returns><see langword="true" /> when the granted permissions strictly include the target's.</returns>
	internal virtual bool Outranks(UserPermissions granted, UserPermissions target)
	{
		return granted.Allows(target) && granted != target;
	}
}

/// <summary>A connection from an osu! game client.</summary>
public sealed class StableConnection : Connection
{
	private readonly Lock _spectating = new();
	private readonly ConcurrentSet<Connection> _spectators = [];

	/// <summary>Initializes a game client connection.</summary>
	/// <param name="login">The login that opened the connection.</param>
	/// <param name="token">The token that identifies the connection.</param>
	/// <param name="utcOffset">The client's UTC offset reported at login.</param>
	/// <param name="context">The runtime dependencies for the connection's behaviour.</param>
	internal StableConnection(SessionRecord login, string token, int utcOffset, ConnectionContext context) : base(
		login,
		token)
	{
		UtcOffset = utcOffset;
		Context = context;
	}

	/// <summary>The runtime dependencies used by this connection.</summary>
	internal ConnectionContext Context { get; }

	/// <inheritdoc />
	public override SessionType Type => SessionType.Stable;

	/// <summary>Gets the client's UTC offset reported at login.</summary>
	public int UtcOffset { get; }

	/// <summary>Gets the presence status the client last reported.</summary>
	public PlayerStatus Status { get; internal set; } = PlayerStatus.Idle;

	/// <summary>Gets the connections spectating this client.</summary>
	public IReadOnlySet<Connection> Spectators => _spectators;

	/// <summary>Gets a value that indicates whether an HTTP API connection is spectating this client.</summary>
	/// <remarks>The host decodes the client's frames only while this is <see langword="true" />.</remarks>
	public bool HasApiSpectators => _spectators.Any(spectator => spectator is ApiConnection);

	/// <summary>
	///     Gets the connections spectating this client, for changing them; the caller holds the scope and reports the
	///     change itself.
	/// </summary>
	internal ISet<Connection> MutableSpectators => _spectators;

	/// <summary>Scopes this client's behaviour to a connection.</summary>
	/// <param name="actor">The connection acting on this client.</param>
	/// <returns>A short-lived scope for this client and connection.</returns>
	public ScopedStableConnection ScopeTo(Connection actor)
	{
		return new ScopedStableConnection(this, actor);
	}

	/// <summary>Enters the scope in which the client's spectators change one at a time.</summary>
	/// <returns>A scope that must be disposed when the change is complete.</returns>
	internal Lock.Scope EnterSpectators()
	{
		return _spectating.EnterScope();
	}

	/// <summary>Removes every spectator when this client goes away and announces each one.</summary>
	internal void ReleaseSpectators()
	{
		using var scope = EnterSpectators();
		var spectators = Spectators.ToArray();
		MutableSpectators.Clear();
		foreach (var spectator in spectators)
			Context.Events.Publish(new UserSpectatorLeft(this, spectator));
	}

	/// <summary>Checks whether this client's user has blocked a connection's user.</summary>
	/// <param name="other">The connection whose user may be blocked.</param>
	/// <returns><see langword="true" /> when the block exists.</returns>
	internal async Task<bool> HasBlockedAsync(Connection other)
	{
		var relationships = await Context.Relationships.ListAsync(Record);
		return relationships.Any(relationship => relationship.Type == RelationshipType.Block &&
		                                         relationship.Target.Equals(other.Record));
	}

	/// <summary>Adds a connection to the spectators of this client.</summary>
	/// <param name="spectator">The connection that spectates.</param>
	/// <returns>The outcome of spectating.</returns>
	/// <remarks>
	///     The spectator stops spectating its previous client first. Spectating this client again announces the
	///     spectator, as an osu! client does once it has the beatmap.
	/// </remarks>
	internal ConnectionOutcome Spectate(Connection spectator)
	{
		// Leave the previous host before entering this host's scope, so two spectator scopes are never held at once.
		var previous = Context.Registry.FindSpectating(spectator);
		if (previous is not null && !ReferenceEquals(previous, this)) previous.StopSpectating(spectator);

		using var scope = EnterSpectators();

		if (!IsOpen) return ConnectionOutcome.TargetOffline;
		// The connection may have closed during the awaits before; check it again before the spectator is added.
		if (!spectator.IsOpen) return ConnectionOutcome.Closed;
		if (spectator.Record.Equals(Record)) return ConnectionOutcome.Self;

		if (!Spectators.Contains(spectator)) MutableSpectators.Add(spectator);
		Context.Events.Publish(new UserSpectatorJoined(this, spectator));
		return ConnectionOutcome.Ok;
	}

	/// <summary>Stops a connection spectating this client.</summary>
	/// <param name="spectator">The connection that stops.</param>
	/// <returns><see langword="true" /> if the connection stopped spectating.</returns>
	internal bool StopSpectating(Connection spectator)
	{
		using var scope = EnterSpectators();
		if (!MutableSpectators.Remove(spectator)) return false;

		Context.Events.Publish(new UserSpectatorLeft(this, spectator));
		return true;
	}

	/// <summary>Reports that a spectator failed to spectate this client.</summary>
	/// <param name="spectator">The connection that failed.</param>
	/// <returns><see langword="true" /> if the connection is spectating this client.</returns>
	internal bool ReportSpectatingFailed(Connection spectator)
	{
		if (!Spectators.Contains(spectator)) return false;

		Context.Events.Publish(new UserSpectatorFailed(this, spectator));
		return true;
	}

	/// <summary>Hands the frames this client sent to the HTTP API connections spectating it.</summary>
	/// <param name="bundle">The decoded frames.</param>
	/// <returns><see langword="true" /> if the frames were published.</returns>
	internal bool PublishFrames(SpectatorBundle bundle)
	{
		if (!IsOpen || !HasApiSpectators) return false;

		Context.Events.Publish(new UserSpectatorFramesReceived(this, bundle));
		return true;
	}

	/// <summary>Records the presence status this client reported.</summary>
	/// <param name="status">The reported status.</param>
	/// <returns><see langword="true" /> if the status changed.</returns>
	internal bool SetStatus(PlayerStatus status)
	{
		if (!IsOpen || Equals(Status, status)) return false;

		Status = status;
		Context.Events.Publish(new UserConnectionStatusChanged(this, status));
		return true;
	}
}

/// <summary>A connection from an osu!tourney spectator client.</summary>
public sealed class TourneyConnection : Connection
{
	/// <summary>Initializes the connection a login opened.</summary>
	/// <param name="login">The login that opened the connection.</param>
	/// <param name="token">The token that identifies the connection.</param>
	internal TourneyConnection(SessionRecord login, string token) : base(login, token)
	{
	}

	/// <inheritdoc />
	public override SessionType Type => SessionType.StableTourney;
}

/// <summary>A connection from an IRC client.</summary>
public sealed class IrcConnection : Connection
{
	/// <summary>Initializes the connection a login opened.</summary>
	/// <param name="login">The login that opened the connection.</param>
	/// <param name="token">The token that identifies the connection.</param>
	internal IrcConnection(SessionRecord login, string token) : base(login, token)
	{
	}

	/// <inheritdoc />
	public override SessionType Type => SessionType.Irc;
}

/// <summary>A connection from a client of the HTTP API.</summary>
public sealed class ApiConnection : Connection
{
	/// <summary>Initializes the connection a login opened.</summary>
	/// <param name="login">The login that opened the connection.</param>
	/// <param name="token">The token that identifies the connection.</param>
	/// <param name="tokenLifetime">How long the token stays valid after each use.</param>
	internal ApiConnection(SessionRecord login, string token, TimeSpan tokenLifetime) : base(login, token)
	{
		TokenLifetime = tokenLifetime;
	}

	/// <summary>Gets how long the token stays valid after each use.</summary>
	public TimeSpan TokenLifetime { get; }

	/// <inheritdoc />
	public override SessionType Type => SessionType.Api;
}

/// <summary>
///     The operator's connection: BasilBot acting for whoever sits at the server's console. It is the one authority
///     that skips the rights checks, because the operator owns the machine; it is never registered, so no client can
///     present it.
/// </summary>
public sealed class OperatorConnection : Connection
{
	/// <summary>Initializes the operator's connection.</summary>
	/// <param name="login">The login that stands for the console.</param>
	/// <param name="token">The token that identifies the connection.</param>
	internal OperatorConnection(SessionRecord login, string token) : base(login, token)
	{
	}

	/// <inheritdoc />
	public override SessionType Type => SessionType.Api;

	/// <inheritdoc />
	public override UserPermissions EffectivePermissions(DateTimeOffset now)
	{
		return UserPermissionsGroups.All;
	}

	/// <inheritdoc />
	internal override bool Outranks(UserPermissions granted, UserPermissions target)
	{
		return true;
	}
}