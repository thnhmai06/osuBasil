using Basil.Application.Sessions;
using Basil.Application.Sessions.Channels;
using Basil.Protocol.Packets;
using Microsoft.Extensions.Logging;

namespace Basil.Application.Services.Spectating;

/// <summary>
///     Manages the spectating channels that connect a userSession and their spectators.
/// </summary>
/// <remarks>
///     Each spectated userSession gets a dedicated <c>#spec_{hostId}</c> instance channel. Its
///     client-visible name is always <c>#spectator</c>, regardless of which host's instance a given
///     client is currently in (see <see cref="ChannelSession" />'s doc comment). The channel is
///     created when the first spectator joins and torn down once the last one leaves. The
///     implementation relies on <see cref="ChannelMembershipService" />'s single channel broadcast
///     and layers only the spectator-specific notifications (spectator joined, fellow spectator
///     joined, and so on) on top; it emits no redundant extra channel-info broadcast.
/// </remarks>
public sealed class SpectatorService(
	IChannelRegistry channelRegistry,
	ChannelMembershipService channelMembership,
	ILogger<SpectatorService> logger)
{
	/// <summary>Builds the instance channel name for a spectated userSession.</summary>
	/// <param name="hostId">The spectated userSession's id.</param>
	/// <returns>The <c>#spec_{id}</c> channel name.</returns>
	private static string ChannelNameFor(int hostId)
	{
		return $"#spec_{hostId}";
	}

	/// <summary>Joins a spectator to a host's spectating channel, notifying the host and fellow spectators.</summary>
	/// <remarks>
	///     Creates the host's spectating channel and joins the host to it when this is the first
	///     spectator. A stealth spectator is only given visibility into existing spectators; the host
	///     and other spectators are never told the userSession joined.
	/// </remarks>
	/// <param name="host">The spectated userSession.</param>
	/// <param name="spectator">The userSession starting to spectate.</param>
	public void AddSpectator(GameSession host, GameSession spectator)
	{
		var channel = channelRegistry.GetByName(ChannelNameFor(host.Id));
		if (channel is null)
		{
			channel = new ChannelSession(
				0, ChannelNameFor(host.Id),
				0, 0, false, "#spectator", true);
			channelRegistry.Add(channel);
			channelMembership.Join(host, channel);
		}

		if (!channelMembership.Join(spectator, channel)) return;

		if (!spectator.Stealth)
		{
			var joinedBySpectator = ServerPacketWriter.FellowSpectatorJoined(spectator.Id);
			foreach (var existing in host.Spectators)
			{
				existing.Enqueue(joinedBySpectator);
				spectator.Enqueue(ServerPacketWriter.FellowSpectatorJoined(existing.Id));
			}

			host.Enqueue(ServerPacketWriter.SpectatorJoined(spectator.Id));
		}
		else
		{
			// Stealth: only give the (admin) spectator visibility into existing spectators, not
			// vice versa: the host and other spectators are never told this userSession joined.
			foreach (var existing in host.Spectators)
				spectator.Enqueue(ServerPacketWriter.FellowSpectatorJoined(existing.Id));
		}

		host.AddSpectator(spectator);
		spectator.Spectating = host;
		logger.LogDebug("Spectator joined: HostId={HostId} SpectatorId={SpectatorId}", host.Id, spectator.Id);
	}

	/// <summary>Parts a spectator from a host's spectating channel, notifying the remaining spectators.</summary>
	/// <remarks>Tears the channel down (parting the host as well) when the last spectator leaves.</remarks>
	/// <param name="host">The spectated userSession.</param>
	/// <param name="spectator">The userSession stopping to spectate.</param>
	public void RemoveSpectator(GameSession host, GameSession spectator)
	{
		host.RemoveSpectator(spectator);
		spectator.Spectating = null;

		var channel = channelRegistry.GetByName(ChannelNameFor(host.Id));
		if (channel is null) return;

		channelMembership.Part(spectator, channel);

		if (host.Spectators.Count == 0)
		{
			channelMembership.Part(host, channel);
			channelRegistry.Remove(channel.Name);
			logger.LogDebug("Spectator left: HostId={HostId} SpectatorId={SpectatorId} ChannelTornDown=true",
				host.Id, spectator.Id);
			return;
		}

		logger.LogDebug("Spectator left: HostId={HostId} SpectatorId={SpectatorId} ChannelTornDown=false",
			host.Id, spectator.Id);

		var fellowLeft = ServerPacketWriter.FellowSpectatorLeft(spectator.Id);
		foreach (var remaining in host.Spectators) remaining.Enqueue(fellowLeft);
	}
}