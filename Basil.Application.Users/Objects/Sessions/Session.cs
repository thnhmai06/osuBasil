using Basil.Application.Shared.Actors;
using Basil.Application.Users.Capabilities;
using Basil.Application.Users.Objects.Events;
using Basil.Domain.Authentication;
using Basil.Domain.Spectating;
using Basil.Domain.Users;
using Basil.Domain.Users;
using Basil.Shared.Structures.Data;

namespace Basil.Application.Users.Objects.Sessions;

public abstract class Session(Domain.Authentication.Session state, string token) : IActor
{
	public User User { get; internal set; } = null!;

	public Domain.Authentication.Session State { get; } = state;

	public string Token { get; } = token;

	public abstract SessionType Type { get; }

	public bool IsOpen { get; internal set; }

	public bool IsServerOwned => Record.IsBasilBot();

	public UserRecord Record => State.Record.Value.User;

	public virtual UserPrivileges EffectivePrivileges(DateTimeOffset now)
	{
		return User.EffectivePrivileges(now);
	}

	internal virtual bool Outranks(UserPrivileges granted, UserPrivileges target)
	{
		return granted.Allows(target) && granted != target;
	}
}

public sealed class StableSession : Session
{
	private readonly Lock _spectating = new();
	private readonly ConcurrentSet<Session> _spectators = [];

	internal StableSession(Domain.Authentication.Session state, string token, int utcOffset,
		SessionContext context) : base(state, token)
	{
		UtcOffset = utcOffset;
		Context = context;
	}

	internal SessionContext Context { get; }

	public override SessionType Type => SessionType.Stable;

	public int UtcOffset { get; }

	public UserStatus Status { get; internal set; } = UserStatus.Idle;

	public IReadOnlySet<Session> Spectators => _spectators;

	public bool HasApiSpectators => _spectators.Any(spectator => spectator is ApiSession);

	internal ISet<Session> MutableSpectators => _spectators;

	public ScopedStableSession ScopeTo(Session actor)
	{
		return new ScopedStableSession(this, actor);
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

	internal async Task<bool> HasBlockedAsync(Session other)
	{
		var relationships = await Context.Relationships.ListAsync(Record);
		return relationships.Any(relationship => relationship.Type == RelationshipType.Block &&
		                                         relationship.Target.Equals(other.Record));
	}

	internal SessionAuthResult Spectate(Session spectator)
	{
		// Leave the previous host before entering this host's scope, so two spectator scopes are never held at once.
		var previous = Context.Registry.FindSpectating(spectator);
		if (previous is not null && !ReferenceEquals(previous, this)) previous.StopSpectating(spectator);

		using var scope = EnterSpectators();

		if (!IsOpen) return SessionAuthResult.TargetOffline;
		// The connection may have closed during the awaits before; check it again before the spectator is added.
		if (!spectator.IsOpen) return SessionAuthResult.Closed;
		if (spectator.Record.Equals(Record)) return SessionAuthResult.Self;

		if (!Spectators.Contains(spectator)) MutableSpectators.Add(spectator);
		Context.Events.Publish(new UserSpectatorJoined(this, spectator));
		return SessionAuthResult.Ok;
	}

	internal bool StopSpectating(Session spectator)
	{
		using var scope = EnterSpectators();
		if (!MutableSpectators.Remove(spectator)) return false;

		Context.Events.Publish(new UserSpectatorLeft(this, spectator));
		return true;
	}

	internal bool ReportSpectatingFailed(Session spectator)
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

	internal bool SetStatus(UserStatus status)
	{
		if (!IsOpen || Equals(Status, status)) return false;

		Status = status;
		Context.Events.Publish(new UserSessionStatusChanged(this, status));
		return true;
	}
}

public sealed class TourneySession : Session
{
	internal TourneySession(Domain.Authentication.Session state, string token) : base(state, token)
	{
	}

	public override SessionType Type => SessionType.StableTourney;
}

public sealed class IrcSession : Session
{
	internal IrcSession(Domain.Authentication.Session state, string token) : base(state, token)
	{
	}

	public override SessionType Type => SessionType.Irc;
}

public sealed class ApiSession : Session
{
	internal ApiSession(Domain.Authentication.Session state, string token) : base(state, token)
	{
	}

	public override SessionType Type => SessionType.Api;
}

public sealed class OperatorSession : Session
{
	internal OperatorSession(Domain.Authentication.Session state, string token) : base(state, token)
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