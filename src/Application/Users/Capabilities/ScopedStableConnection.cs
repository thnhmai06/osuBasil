using Basil.Application.Shared.Privileges;
using Basil.Application.Users.Objects.Connections;
using Basil.Domain.Game.Client;
using Basil.Domain.Users;
using Basil.Domain.Game.Spectating;
using Basil.Domain.Game.Users;

namespace Basil.Application.Users.Capabilities;

public readonly struct ScopedStableConnection
{
	public Connection Actor { get; }

	public UserPrivileges Privileges { get; }

	internal bool Lacks(UserPrivileges required)
	{
		return ScopedPrivileges.Lacks(Privileges, required);
	}

	internal ScopedStableConnection(StableConnection host, Connection actor)
	{
		Actor = actor;
		Privileges = actor.EffectivePrivileges(host.Context.Clock.GetUtcNow());
		Host = host;
	}

	public StableConnection Host { get; }

	public async Task<ConnectionOutcome> SpectateAsync()
	{
		if (Actor is IrcConnection)
			throw new ArgumentException("IRC clients cannot spectate.");

		if (Lacks(UserPrivileges.Player)) return ConnectionOutcome.Denied;

		if (await Host.HasBlockedAsync(Actor) && Lacks(UserPrivileges.Tournament))
			return ConnectionOutcome.Blocked;

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