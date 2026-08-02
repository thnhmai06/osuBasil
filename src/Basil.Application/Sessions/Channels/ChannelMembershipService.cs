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
public sealed class ChannelMembershipService(IUserSessionRegistry sessionRegistry, IChannelRegistry channelRegistry)
{
	/// <summary>
	///     Adds a userSession to a channel and broadcasts the join to existing members, returning false
	///     when the userSession has already joined or lacks read access.
	/// </summary>
	/// <param name="userSession">The session of the userSession joining the channel.</param>
	/// <param name="channel">The channel to join.</param>
	/// <returns><see langword="true" /> if the userSession was added to the channel; otherwise, <see langword="false" />.</returns>
	public bool Join(UserSession userSession, ChannelSession channel)
	{
		if (userSession.InChannel(channel.Name) || !channel.CanRead(userSession.Privilege)) return false;

		channel.Join(userSession.Id);
		userSession.JoinChannel(channel.Name);
		userSession.Enqueue(ServerPacketWriter.ChannelJoin(channel.DisplayName));

		BroadcastChannelInfo(channel);

		var joinMessage = IrcMessageWriter.Join(userSession.Name, userSession.Id, channel.Name);
		foreach (var memberId in channel.MemberIds)
			sessionRegistry.GetById(memberId)?.IrcConnection.Send(joinMessage);

		return true;
	}

	/// <summary>
	///     Removes a userSession from a channel and broadcasts the part to the remaining members.
	/// </summary>
	/// <param name="userSession">The session of the userSession leaving the channel.</param>
	/// <param name="channel">The channel to leave.</param>
	/// <param name="kick">
	///     When <see langword="true" />, also sends the userSession a ChannelKick packet so the client drops the
	///     channel from its chat list; when <see langword="false" />, the userSession leaves silently.
	/// </param>
	public void Part(UserSession userSession, ChannelSession channel, bool kick = true)
	{
		if (!userSession.InChannel(channel.Name)) return;

		var partMessage = IrcMessageWriter.Part(userSession.Name, userSession.Id, channel.Name);
		foreach (var memberId in channel.MemberIds)
			sessionRegistry.GetById(memberId)?.IrcConnection.Send(partMessage);

		channel.Part(userSession.Id);
		userSession.LeaveChannel(channel.Name);

		if (kick) userSession.Enqueue(ServerPacketWriter.ChannelKick(channel.DisplayName));

		BroadcastChannelInfo(channel);
	}

	/// <summary>
	///     Cleans up every channel <paramref name="userSession" /> is in and notifies the remaining
	///     IRC-shaped connections with a single QUIT each (deduplicated across shared channels).
	///     Called when a real IRC TCP connection disconnects; bancho sessions never call this,
	///     since they leave via GhostDisconnectService and bancho clients only ever saw ChannelInfo
	///     counts rather than per-user quit events.
	/// </summary>
	/// <param name="userSession">The session of the disconnecting userSession.</param>
	/// <param name="reason">The quit reason reported to the remaining members.</param>
	public void Quit(UserSession userSession, string reason)
	{
		var quitMessage = IrcMessageWriter.Quit(userSession.Name, userSession.Id, reason);
		var notified = new HashSet<int>();

		foreach (var channel in userSession.Channels.Select(channelRegistry.GetByName).OfType<ChannelSession>())
		{
			foreach (var memberId in channel.MemberIds)
				if (memberId != userSession.Id && notified.Add(memberId))
					sessionRegistry.GetById(memberId)?.IrcConnection.Send(quitMessage);

			channel.Part(userSession.Id);
			userSession.LeaveChannel(channel.Name);
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