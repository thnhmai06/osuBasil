using Basil.Application.Shared.Privileges;
using Basil.Application.Users.Objects.Sessions;
using Basil.Domain.Game.Client;
using Basil.Domain.Users;
using Basil.Domain.Game.Spectating;
using Basil.Domain.Game.Users;

namespace Basil.Application.Users.Capabilities;

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

	public async Task<SessionOutcome> SpectateAsync()
	{
		if (Actor is IrcSession)
			throw new ArgumentException("IRC clients cannot spectate.");

		if (Lacks(UserPrivileges.Player)) return SessionOutcome.Denied;

		if (await Host.HasBlockedAsync(Actor) && Lacks(UserPrivileges.Tournament))
			return SessionOutcome.Blocked;

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

	public bool SetStatus(PlayerStatus status)
	{
		return ReferenceEquals(Actor, Host) && Host.SetStatus(status);
	}
}