using Basil.Application.Services.Contracts.Chat;
using Basil.Application.Services.Contracts.Sessions;
using Basil.Application.Services.Implementations.Users;
using Basil.Domain.Users;

namespace Basil.Application.Services.Implementations.Sessions;

/// <summary>Starts and stops spectating osu! clients.</summary>
internal sealed class BanchoSpectatorsService(
	SessionEventStream events,
	IUserRegistry users,
	IChannelService channels,
	TimeProvider time) : IBanchoSpectatorsService
{
	/// <inheritdoc />
	public SpectateResult Spectate(BanchoConnection host, Connection by)
	{
		if (by is IrcConnection) throw new ArgumentException("IRC clients cannot spectate.", nameof(by));
		if (!PermissionRules.Allows(by, Permissions.SpectatingWatch, time.GetUtcNow()))
			return SpectateResult.NotPermitted;

		// Leave the previous host before entering this host's scope, so two spectator scopes are never held at once.
		var previous = users.FindSpectating(by);
		if (previous is not null && !ReferenceEquals(previous, host)) StopSpectating(by);

		using var scope = host.EnterSpectators();

		if (!host.IsOpen) return SpectateResult.TargetOffline;
		if (by.User.Equals(host.User)) return SpectateResult.Self;
		if (host.Spectators.Contains(by))
		{
			events.Emit(new UserSpectatorJoined(host, by));
			return SpectateResult.Spectating;
		}

		var first = host.Spectators.Count == 0;
		host.MutableSpectators.Add(by);
		if (first) channels.Members.Join(host.SpectatorChannel, host);
		channels.Members.Join(host.SpectatorChannel, by);
		events.Emit(new UserSpectatorJoined(host, by));
		return SpectateResult.Spectating;
	}

	/// <inheritdoc />
	public bool StopSpectating(Connection by)
	{
		var host = users.FindSpectating(by);
		if (host is null) return false;

		using var scope = host.EnterSpectators();
		if (!host.MutableSpectators.Remove(by)) return false;

		channels.Members.Part(host.SpectatorChannel, by);
		if (host.Spectators.Count == 0) channels.Members.Part(host.SpectatorChannel, host);
		events.Emit(new UserSpectatorLeft(host, by));
		return true;
	}

	/// <inheritdoc />
	public bool ReportSpectatingFailed(Connection by)
	{
		var host = users.FindSpectating(by);
		if (host is null) return false;

		events.Emit(new UserSpectatorFailed(host, by));
		return true;
	}
}
