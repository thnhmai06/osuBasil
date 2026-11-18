using Basil.Application.Shared.Actors;
using Basil.Application.Users.Capabilities;
using Basil.Application.Users.Objects.Events;
using Basil.Domain.Authentication;
using Basil.Domain.Game.Client;

using Basil.Domain.Users;
using Basil.Domain.Game.Spectating;
using Basil.Domain.Game.Users;
using Basil.Shared.Structures.Data;

namespace Basil.Application.Users.Objects.Connections;

public abstract class Connection(SessionRecord login, string token) : IActor
{
	// Swapped whole: a connection and the activity flush touch it from different threads.
	private Activity _activity = new(login.Value.LastActiveAt, login.Value.ExpiresAt);
	private SessionEnd? _end;

	public User User { get; internal set; } = null!;

	public SessionRecord Session { get; } = login;

	public string Token { get; } = token;

	public abstract SessionType Type { get; }

	public bool IsOpen { get; internal set; }

	public DateTimeOffset LastActiveAt => Volatile.Read(ref _activity).LastActiveAt;

	public DateTimeOffset? ExpiresAt => Volatile.Read(ref _activity).ExpiresAt;

	public SessionEnd? End => Volatile.Read(ref _end);

	public bool IsServerOwned => Record.IsBasilBot();

	public UserRecord Record => Session.Value.User;

	public virtual UserPrivileges EffectivePrivileges(DateTimeOffset now)
	{
		return User.EffectivePrivileges(now);
	}

	public SessionRecord Snapshot()
	{
		var activity = Volatile.Read(ref _activity);
		var value = Session.Value;
		return new SessionRecord
		{
			Id = Session.Id,
			Value = value with
			{
				ExpiresAt = activity.ExpiresAt,
				LastActiveAt = activity.LastActiveAt,
				End = End
			}
		};
	}

	internal void MarkActive(DateTimeOffset at)
	{
		while (true)
		{
			var current = Volatile.Read(ref _activity);
			if (End is not null || at < current.LastActiveAt) return;
			if (Interlocked.CompareExchange(ref _activity, current with { LastActiveAt = at }, current) ==
			    current) return;
		}
	}

	internal void ExtendExpiry(DateTimeOffset at, TimeSpan lifetime)
	{
		while (true)
		{
			var current = Volatile.Read(ref _activity);
			if (Type is not SessionType.Api || End is not null) return;
			if (Interlocked.CompareExchange(ref _activity, current with { ExpiresAt = at + lifetime }, current) ==
			    current) return;
		}
	}

	internal void EndSession(DateTimeOffset at, SessionEndReason reason)
	{
		Interlocked.CompareExchange(ref _end, new SessionEnd(at, reason), null);
	}

	internal virtual bool Outranks(UserPrivileges granted, UserPrivileges target)
	{
		return granted.Allows(target) && granted != target;
	}

	private sealed record Activity(DateTimeOffset LastActiveAt, DateTimeOffset? ExpiresAt);
}

public sealed class StableConnection : Connection
{
	private readonly Lock _spectating = new();
	private readonly ConcurrentSet<Connection> _spectators = [];

	internal StableConnection(SessionRecord login, string token, int utcOffset, ConnectionContext context) : base(
		login,
		token)
	{
		UtcOffset = utcOffset;
		Context = context;
	}

	internal ConnectionContext Context { get; }

	public override SessionType Type => SessionType.Stable;

	public int UtcOffset { get; }

	public PlayerStatus Status { get; internal set; } = PlayerStatus.Idle;

	public IReadOnlySet<Connection> Spectators => _spectators;

	public bool HasApiSpectators => _spectators.Any(spectator => spectator is ApiConnection);

	internal ISet<Connection> MutableSpectators => _spectators;

	public ScopedStableConnection ScopeTo(Connection actor)
	{
		return new ScopedStableConnection(this, actor);
	}

	internal Lock.Scope EnterSpectators()
	{
		return _spectating.EnterScope();
	}

	internal void ReleaseSpectators()
	{
		using var scope = EnterSpectators();
		var spectators = Spectators.ToArray();
		MutableSpectators.Clear();
		foreach (var spectator in spectators)
			Context.Events.Publish(new UserSpectatorLeft(this, spectator));
	}

	internal async Task<bool> HasBlockedAsync(Connection other)
	{
		var relationships = await Context.Relationships.ListAsync(Record);
		return relationships.Any(relationship => relationship.Type == RelationshipType.Block &&
		                                         relationship.Target.Equals(other.Record));
	}

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

	internal bool StopSpectating(Connection spectator)
	{
		using var scope = EnterSpectators();
		if (!MutableSpectators.Remove(spectator)) return false;

		Context.Events.Publish(new UserSpectatorLeft(this, spectator));
		return true;
	}

	internal bool ReportSpectatingFailed(Connection spectator)
	{
		if (!Spectators.Contains(spectator)) return false;

		Context.Events.Publish(new UserSpectatorFailed(this, spectator));
		return true;
	}

	internal bool PublishFrames(SpectatorBundle bundle)
	{
		if (!IsOpen || !HasApiSpectators) return false;

		Context.Events.Publish(new UserSpectatorFramesReceived(this, bundle));
		return true;
	}

	internal bool SetStatus(PlayerStatus status)
	{
		if (!IsOpen || Equals(Status, status)) return false;

		Status = status;
		Context.Events.Publish(new UserConnectionStatusChanged(this, status));
		return true;
	}
}

public sealed class TourneyConnection : Connection
{
	internal TourneyConnection(SessionRecord login, string token) : base(login, token)
	{
	}

	public override SessionType Type => SessionType.StableTourney;
}

public sealed class IrcConnection : Connection
{
	internal IrcConnection(SessionRecord login, string token) : base(login, token)
	{
	}

	public override SessionType Type => SessionType.Irc;
}

public sealed class ApiConnection : Connection
{
	internal ApiConnection(SessionRecord login, string token, TimeSpan tokenLifetime) : base(login, token)
	{
		TokenLifetime = tokenLifetime;
	}

	public TimeSpan TokenLifetime { get; }

	public override SessionType Type => SessionType.Api;
}

public sealed class OperatorConnection : Connection
{
	internal OperatorConnection(SessionRecord login, string token) : base(login, token)
	{
	}

	public override SessionType Type => SessionType.Api;

	public override UserPrivileges EffectivePrivileges(DateTimeOffset now)
	{
		return UserPrivilegesExtensions.All;
	}

	internal override bool Outranks(UserPrivileges granted, UserPrivileges target)
	{
		return true;
	}
}