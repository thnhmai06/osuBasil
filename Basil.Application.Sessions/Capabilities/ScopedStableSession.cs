using Basil.Application.Shared.Privileges;
using Basil.Application.Sessions.Messages;
using Basil.Application.Sessions.Objects;
using Basil.Domain.Game;
using Basil.Domain.Spectating;
using Basil.Domain.Users;
using Session = Basil.Application.Sessions.Objects.Session;

namespace Basil.Application.Sessions.Capabilities;

public readonly struct ScopedStableSession
{
	public Session Actor { get; }

	public UserPrivileges Privileges { get; }

	internal bool Lacks(UserPrivileges required)
	{
		return ScopedPrivileges.Lacks(Privileges, required);
	}

	internal ScopedStableSession(StableSession host, Session actor)
	{
		Actor = actor;
		Privileges = actor.EffectivePrivileges(host.Context.Clock.GetUtcNow());
		Host = host;
	}

	public StableSession Host { get; }

	public async Task<SessionAuthResult> SpectateAsync()
	{
		if (Actor is IrcSession)
			throw new ArgumentException("IRC clients cannot spectate.");

		if (Lacks(UserPrivileges.Player)) return SessionAuthResult.Denied;

		if (await Host.HasBlockedAsync(Actor) && Lacks(UserPrivileges.Tournament))
			return SessionAuthResult.Blocked;

		return Host.Spectate(Actor);
	}

	public bool StopSpectating()
	{
		return Host.StopSpectating(Actor);
	}

	public bool ReportSpectatingFailed()
	{
		return Host.ReportSpectatingFailed(Actor);
	}

	public bool PublishFrames(SpectatorBundle bundle)
	{
		return ReferenceEquals(Actor, Host) && Host.PublishFrames(bundle);
	}

	/// <summary>Gets who the host's spectator frames are relayed to; nobody when the actor is not the host.</summary>
	public SpectatorRelay Relay()
	{
		return ReferenceEquals(Actor, Host) ? new SpectatorRelay(Host.Spectators, Host.HasApiSpectators) : SpectatorRelay.None;
	}

	public bool SetPresenceFilter(PresenceFilter filter)
	{
		if (!ReferenceEquals(Actor, Host) || Host.PresenceFilter == filter) return false;

		Host.PresenceFilter = filter;
		return true;
	}

	public bool SetStatus(UserStatus status)
	{
		return ReferenceEquals(Actor, Host) && Host.SetStatus(status);
	}
}