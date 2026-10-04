using Basil.Application.Contracts.Chat;
using Basil.Application.Storage.Sessions;

namespace Basil.Application.Services.Chat;

/// <summary>Starts and stops spectating osu! players.</summary>
internal sealed class ChannelSpectatorService(ChannelEventStream events, UserRegistry users) : IChannelSpectatorService
{
	/// <inheritdoc />
	public SpectateResult Spectate(BanchoConnection host, Connection by)
	{
		if (by is not (BanchoConnection or TourneyConnection or BotConnection))
			throw new ArgumentException("Only osu!, osu!tourney clients and BasilBot can spectate.", nameof(by));

		var spectatorChannel = host.SpectatorChannel;
		using var scope = spectatorChannel.Enter();

		if (!host.IsOpen) return SpectateResult.TargetOffline;
		if (by.User.Equals(host.User)) return SpectateResult.Self;
		if (spectatorChannel.Members.Contains(by)) return SpectateResult.AlreadySpectating;

		var hostJoined = spectatorChannel.AddMember(host);
		spectatorChannel.AddMember(by);
		events.Emit(new ChannelSpectatorJoined(spectatorChannel, by, hostJoined));
		return SpectateResult.Spectating;
	}

	/// <inheritdoc />
	public bool StopSpectating(Connection by)
	{
		var spectatorChannel = users.FindSpectating(by);
		if (spectatorChannel is null) return false;

		using var scope = spectatorChannel.Enter();
		if (ReferenceEquals(by, spectatorChannel.Host) || !spectatorChannel.RemoveMember(by)) return false;

		var hostLeft = !spectatorChannel.Spectators.Any() && spectatorChannel.RemoveMember(spectatorChannel.Host);
		events.Emit(new ChannelSpectatorLeft(spectatorChannel, by, hostLeft));
		return true;
	}

	/// <inheritdoc />
	public bool ReportSpectatingFailed(Connection by)
	{
		var spectatorChannel = users.FindSpectating(by);
		if (spectatorChannel is null) return false;

		events.Emit(new ChannelSpectatorFailed(spectatorChannel, by));
		return true;
	}
}
