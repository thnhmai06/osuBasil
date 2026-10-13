using Basil.Application.Services.Contracts.Chat;
using Basil.Application.Services.Contracts.Sessions;
using Basil.Application.Services.Implementations.Users;
using Basil.Application.Storage.Contracts.Sessions;
using Basil.Domain.Users;

namespace Basil.Application.Services.Implementations.Sessions;

/// <summary>Starts and stops spectating osu! clients.</summary>
internal sealed class SessionSpectatorsService(
	SessionEventStream events,
	IUserRegistry users,
	IChannelService channels,
	TimeProvider time) : ISessionSpectatorsService
{
	/// <inheritdoc />
	public SpectateResult Spectate(BanchoConnection host, Connection by)
	{
		if (by is IrcConnection) throw new ArgumentException("IRC clients cannot spectate.", nameof(by));
		if (!PermissionRules.Allows(by, Permissions.PlayerSpectate, time.GetUtcNow())) return SpectateResult.NotPermitted;

		using var scope = host.EnterSpectators();

		if (!host.IsOpen) return SpectateResult.TargetOffline;
		if (by.User.Equals(host.User)) return SpectateResult.Self;
		if (host.Spectators.Contains(by)) return SpectateResult.AlreadySpectating;

		var first = host.Spectators.Count == 0;
		host.MutableSpectators.Add(by);
		if (first) channels.Join(host.SpectatorChannel, host);
		channels.Join(host.SpectatorChannel, by);
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

		channels.Part(host.SpectatorChannel, by);
		if (host.Spectators.Count == 0) channels.Part(host.SpectatorChannel, host);
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
