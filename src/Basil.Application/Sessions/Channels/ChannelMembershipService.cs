using Basil.Protocol.Irc;
using Basil.Protocol.Packets;

namespace Basil.Application.Sessions.Channels;

/// <summary>
///     Provides the shared join and part logic for channels, used by both client-initiated
///     CHANNEL_JOIN and CHANNEL_PART packets and server-initiated instance membership such as
///     spectator channels. Broadcast scope differs by channel kind: an instance channel only
///     notifies its own current members, while an ordinary channel notifies every session that can
///     read it. Also owns the IRC-shaped JOIN, PART, QUIT, and PRIVMSG broadcast primitives, kept
///     dependency-free of <c>ICommandDispatcher</c> on purpose: command dispatch lives one layer up
///     in <c>ChatDispatchService</c>, and referencing the dispatcher here would create a dependency
///     cycle (CommandDispatcher to MpCommandService to MatchMembershipService to this class).
/// </summary>
public sealed class ChannelMembershipService(IPlayerSessionRegistry sessionRegistry, IChannelRegistry channelRegistry)
{
	/// <summary>
	///     Adds a player to a channel and broadcasts the join to existing members, returning false
	///     when the player has already joined or lacks read access.
	/// </summary>
	/// <param name="player">The session of the player joining the channel.</param>
	/// <param name="channel">The channel to join.</param>
	/// <returns><see langword="true" /> if the player was added to the channel; otherwise, <see langword="false" />.</returns>
	public bool Join(PlayerSession player, ChannelSession channel)
	{
		if (player.InChannel(channel.Name) || !channel.CanRead(player.Privilege)) return false;

		channel.Join(player.Id);
		player.JoinChannel(channel.Name);
		player.Enqueue(ServerPacketWriter.ChannelJoin(channel.DisplayName));

		BroadcastChannelInfo(channel);

		var joinMessage = IrcMessageWriter.Join(player.Name, player.Id, channel.Name);
		foreach (var memberId in channel.MemberIds)
			sessionRegistry.GetById(memberId)?.IrcConnection.Send(joinMessage);

		return true;
	}

	/// <summary>
	///     Removes a player from a channel and broadcasts the part to the remaining members.
	/// </summary>
	/// <param name="player">The session of the player leaving the channel.</param>
	/// <param name="channel">The channel to leave.</param>
	/// <param name="kick">
	///     When <see langword="true" />, also sends the player a ChannelKick packet so the client drops the
	///     channel from its chat list; when <see langword="false" />, the player leaves silently.
	/// </param>
	public void Part(PlayerSession player, ChannelSession channel, bool kick = true)
	{
		if (!player.InChannel(channel.Name)) return;

		var partMessage = IrcMessageWriter.Part(player.Name, player.Id, channel.Name);
		foreach (var memberId in channel.MemberIds)
			sessionRegistry.GetById(memberId)?.IrcConnection.Send(partMessage);

		channel.Part(player.Id);
		player.LeaveChannel(channel.Name);

		if (kick) player.Enqueue(ServerPacketWriter.ChannelKick(channel.DisplayName));

		BroadcastChannelInfo(channel);
	}

	/// <summary>
	///     Cleans up every channel <paramref name="player" /> is in and notifies the remaining
	///     IRC-shaped connections with a single QUIT each (deduplicated across shared channels).
	///     Called when a real IRC TCP connection disconnects; bancho sessions never call this,
	///     since they leave via GhostDisconnectService and bancho clients only ever saw ChannelInfo
	///     counts rather than per-user quit events.
	/// </summary>
	/// <param name="player">The session of the disconnecting player.</param>
	/// <param name="reason">The quit reason reported to the remaining members.</param>
	public void Quit(PlayerSession player, string reason)
	{
		var quitMessage = IrcMessageWriter.Quit(player.Name, player.Id, reason);
		var notified = new HashSet<int>();

		foreach (var channelName in player.Channels.ToList())
		{
			var channel = channelRegistry.GetByName(channelName);
			if (channel is null) continue;

			foreach (var memberId in channel.MemberIds)
				if (memberId != player.Id && notified.Add(memberId))
					sessionRegistry.GetById(memberId)?.IrcConnection.Send(quitMessage);

			channel.Part(player.Id);
			player.LeaveChannel(channel.Name);
			BroadcastChannelInfo(channel);
		}
	}

	/// <summary>
	///     Sends raw packet bytes to every session currently in the channel, not everyone who merely
	///     can read it, optionally skipping the given immune set. Multiplayer routes
	///     match.enqueue and enqueue_state through the match's chat channel by calling this.
	/// </summary>
	/// <param name="channel">The channel whose members receive the packet.</param>
	/// <param name="packet">The raw bancho packet bytes to enqueue on each member's session.</param>
	/// <param name="immune">The set of member ids to skip, or null to broadcast to everyone.</param>
	public void BroadcastToMembers(ChannelSession channel, byte[] packet, IReadOnlyCollection<int>? immune = null)
	{
		foreach (var memberId in channel.MemberIds)
		{
			if (immune is not null && immune.Contains(memberId)) continue;

			sessionRegistry.GetById(memberId)?.Enqueue(packet);
		}
	}

	/// <summary>
	///     The IRC-shaped counterpart of <see cref="BroadcastToMembers" /> for chat text specifically.
	///     Routes through each member's <see cref="Sessions.Irc.IIrcConnection" /> rather than a raw
	///     bancho packet, so it reaches real IRC clients and bancho clients alike.
	/// </summary>
	/// <param name="channel">The channel whose members receive the message.</param>
	/// <param name="message">The IRC-shaped message to deliver.</param>
	/// <param name="skipMemberId">The id of a member to skip, typically the message's sender, or null to deliver to everyone.</param>
	public void BroadcastPrivmsg(ChannelSession channel, IrcMessage message, int? skipMemberId = null)
	{
		foreach (var memberId in channel.MemberIds)
		{
			if (memberId == skipMemberId) continue;

			sessionRegistry.GetById(memberId)?.IrcConnection.Send(message);
		}
	}

	private void BroadcastChannelInfo(ChannelSession channel)
	{
		var packet = ServerPacketWriter.ChannelInfo(channel.DisplayName, channel.Topic, channel.PlayerCount);

		if (channel.Instance)
			foreach (var memberId in channel.MemberIds)
				sessionRegistry.GetById(memberId)?.Enqueue(packet);
		else
			foreach (var session in sessionRegistry.All)
				if (channel.CanRead(session.Privilege))
					session.Enqueue(packet);
	}
}