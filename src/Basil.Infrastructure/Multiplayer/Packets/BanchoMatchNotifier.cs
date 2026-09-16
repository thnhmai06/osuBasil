using Basil.Application.Multiplayer;
using Basil.Application.Sessions;
using Basil.Application.Shared.Eventing;
using Basil.Domain.Channels;
using Basil.Infrastructure.Chat;
using Basil.Infrastructure.Shared.Sessions;
using Basil.Protocol.Packets;

namespace Basil.Infrastructure.Multiplayer.Packets;

/// <summary>
///     Delivers match notifications to osu! clients as bancho packets: to one player's session, to
///     every member of the match's chat channel, or to the lobby channel when it has anyone in it.
/// </summary>
public sealed class BanchoMatchNotifier(
	IChannelRegistry channelRegistry,
	ChannelMembershipService channelMembership) : IMatchNotifier
{
	private static readonly KeyValuePair<string, object?> PacketStreamTag = new("stream", "packet");

	public void JoinRejected(GameSession player)
	{
		player.Enqueue(ServerPacketWriter.MatchJoinFail());
	}

	public void Removed(GameSession player)
	{
		player.Enqueue(ServerPacketWriter.MatchJoinFail());
	}

	public void Joined(GameSession player, MatchSession match)
	{
		player.Enqueue(ServerPacketWriter.MatchJoinSuccess(match.ToPacket()));
	}

	public void HostTransferred(GameSession newHost)
	{
		newHost.Enqueue(ServerPacketWriter.MatchTransferHost());
	}

	public void Invited(GameSession target, UserSession sender, MatchSession match)
	{
		target.Enqueue(ServerPacketWriter.MatchInvite(sender.Id, sender.Name, match.Embed, target.Name));
	}

	public void RoundStarted(MatchSession match, IReadOnlyCollection<int> notPlaying)
	{
		Broadcast(match, ServerPacketWriter.MatchStart(match.ToPacket()), false, notPlaying);
	}

	public void RoundAborted(MatchSession match)
	{
		Broadcast(match, ServerPacketWriter.MatchAbort(), false);
	}

	public void Disposed(MatchSession match)
	{
		var lobby = channelRegistry.GetByName("#lobby");
		if (lobby is not null) channelMembership.BroadcastToMembers(lobby, ServerPacketWriter.DisposeMatch(match.Id));
	}

	public void StateChanged(MatchSession match, long version, bool lobby)
	{
		if (!match.PacketBroadcastGate.TryAdvance(version))
		{
			EventingMetrics.StalePublishDropped.Add(1, PacketStreamTag);
			return;
		}

		var channel = channelRegistry.GetByName(match.ChatChannelName);
		if (channel is not null)
			channelMembership.BroadcastToMembers(channel, ServerPacketWriter.UpdateMatch(match.ToPacket()));

		// The lobby sees the room without its password.
		if (!match.IsPrivate) BroadcastToNonEmptyLobby(ServerPacketWriter.UpdateMatch(match.ToPacket(), false), lobby);
	}

	/// <summary>Sends a packet to the match channel and, for public rooms, the non-empty lobby.</summary>
	/// <param name="match">The match whose channel to send into.</param>
	/// <param name="data">The serialized packet bytes.</param>
	/// <param name="lobby"><see langword="true" /> to also send to the lobby; otherwise, <see langword="false" />.</param>
	/// <param name="immune">User ids to leave out, or <see langword="null" /> for none.</param>
	public void Broadcast(MatchSession match, byte[] data, bool lobby = true, IReadOnlyCollection<int>? immune = null)
	{
		var channel = channelRegistry.GetByName(match.ChatChannelName);
		if (channel is not null) channelMembership.BroadcastToMembers(channel, data, immune);

		if (!match.IsPrivate) BroadcastToNonEmptyLobby(data, lobby);
	}

	private void BroadcastToNonEmptyLobby(byte[] data, bool lobby)
	{
		if (!lobby) return;

		var lobbyChannel = channelRegistry.GetByName("#lobby");
		if (lobbyChannel is not null && lobbyChannel.PlayerCount > 0)
			channelMembership.BroadcastToMembers(lobbyChannel, data);
	}
}
