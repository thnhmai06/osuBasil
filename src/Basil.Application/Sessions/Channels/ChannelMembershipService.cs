using Basil.Application.Configurations;
using Basil.Domain.Users;
using Basil.Protocol.Irc;
using Basil.Protocol.Packets;
using Microsoft.Extensions.Options;

namespace Basil.Application.Sessions.Channels;

/// <summary>
///     Provides the shared join and part logic for channels, used by both client-initiated
///     CHANNEL_JOIN and CHANNEL_PART packets and server-initiated instance membership such as
///     spectator channels. Broadcast scope differs by channel kind: an instance channel only
///     notifies its own current members, while an ordinary channel notifies every <see cref="GameSession" />
///     that can read it. Also owns the IRC-shaped JOIN, PART, and PRIVMSG broadcast primitives, kept
///     dependency-free of <c>ICommandDispatcher</c> on purpose: command dispatch lives one layer up
///     in <c>ChatDispatchService</c>, and referencing the dispatcher here would create a dependency
///     cycle (CommandDispatcher to MpCommandService to MatchMembershipService to this class).
/// </summary>
/// <remarks>
///     An account may hold both a <see cref="GameSession" /> and an <see cref="IrcSession" /> at
///     once. Channel membership tracks each session's presence separately
///     (<see cref="UserSession.JoinChannel" />/<see cref="UserSession.LeaveChannel" />), but the
///     channel's own roster (<see cref="ChannelSession.MemberIds" />) counts a UserId once no matter
///     how many of its sessions are present — <see cref="ChannelSession.Join" />/
///     <see cref="ChannelSession.Part" /> report whether this changed. Only a roster change is
///     broadcast to other members; each joining/parting session still receives its own echo
///     regardless, so a second session for an already-present UserId still confirms locally without
///     spamming everyone else with a redundant JOIN/PART.
/// </remarks>
public sealed class ChannelMembershipService(
	ISessionRegistry<GameSession> gameRegistry,
	ISessionRegistry<IrcSession> ircRegistry,
	IChannelRegistry channelRegistry,
	IOptions<IrcOptions> options)
{
	/// <summary>
	///     Adds a userSession to a channel, echoing the join to the session itself and, only when this
	///     is the first session of its UserId present, broadcasting to the channel's other members.
	/// </summary>
	/// <param name="userSession">The session of the userSession joining the channel.</param>
	/// <param name="channel">The channel to join.</param>
	/// <returns><see langword="true" /> if the userSession was added to the channel; otherwise, <see langword="false" />.</returns>
	public bool Join(UserSession userSession, ChannelSession channel)
	{
		if (!channel.CanRead(userSession.Privilege)) return false;
		if (!userSession.JoinChannel(channel.Name)) return false;

		var userEnteredRoster = channel.Join(userSession.Id);

		switch (userSession)
		{
			case GameSession game:
				game.Enqueue(ServerPacketWriter.ChannelJoin(channel.DisplayName));
				break;
			case IrcSession irc:
				irc.IrcConnection.Send(IrcMessageWriter.Join(irc.Name, irc.Id, channel.Name));
				foreach (var reply in BuildNamesReply(irc.Name, channel)) irc.IrcConnection.Send(reply);
				break;
		}

		if (userEnteredRoster)
		{
			BroadcastChannelInfo(channel);
			BroadcastToOtherIrcMembers(channel, userSession.Id,
				IrcMessageWriter.Join(userSession.Name, userSession.Id, channel.Name));
		}

		return true;
	}

	/// <summary>
	///     Removes a userSession from a channel, echoing the part to the session itself and, only when
	///     this was the last session of its UserId present, broadcasting to the channel's other
	///     members. Used for a userSession-initiated part (PART command, CHANNEL_PART packet, kick out
	///     of a match's chat) — a disconnecting session leaves through <see cref="DisconnectFromChannels" />
	///     instead, which applies PART/QUIT rules across every joined channel at once.
	/// </summary>
	/// <param name="userSession">The session of the userSession leaving the channel.</param>
	/// <param name="channel">The channel to leave.</param>
	/// <param name="kick">
	///     When <see langword="true" />, also sends a <see cref="GameSession" /> a ChannelKick packet so the client
	///     drops the channel from its chat list; when <see langword="false" />, the userSession leaves silently.
	/// </param>
	public void Part(UserSession userSession, ChannelSession channel, bool kick = true)
	{
		if (!userSession.LeaveChannel(channel.Name)) return;

		var userLeftRoster = channel.Part(userSession.Id);

		switch (userSession)
		{
			case GameSession game when kick:
				game.Enqueue(ServerPacketWriter.ChannelKick(channel.DisplayName));
				break;
			case IrcSession irc:
				irc.IrcConnection.Send(IrcMessageWriter.Part(irc.Name, irc.Id, channel.Name));
				break;
		}

		if (!userLeftRoster) return;

		BroadcastChannelInfo(channel);
		BroadcastToOtherIrcMembers(channel, userSession.Id,
			IrcMessageWriter.Part(userSession.Name, userSession.Id, channel.Name));
	}

	/// <summary>
	///     Removes a disconnecting session from every channel it had joined, applying PART/QUIT rules
	///     based on whether the same UserId is still present elsewhere: no event when another of the
	///     UserId's sessions remains in that same channel, a PART for a channel it fully leaves while
	///     the UserId is still present somewhere else in the chat system, or — when this was the
	///     UserId's last session anywhere — a single deduplicated QUIT instead of any PART.
	/// </summary>
	/// <param name="session">The session that is disconnecting.</param>
	/// <param name="quitReason">The reason reported to remaining members if a QUIT is sent.</param>
	public void DisconnectFromChannels(UserSession session, string quitReason)
	{
		var otherGame = gameRegistry.GetByUserId(session.Id);
		var otherIrc = ircRegistry.GetByUserId(session.Id);
		var userStillPresent = (otherGame is not null && !ReferenceEquals(otherGame, session))
		                       || (otherIrc is not null && !ReferenceEquals(otherIrc, session));
		var quitMessage = IrcMessageWriter.Quit(session.Name, session.Id, quitReason);
		var quitNotified = new HashSet<int>();

		foreach (var channelName in session.Channels.ToArray())
		{
			if (channelRegistry.GetByName(channelName) is not { } channel) continue;
			if (!session.LeaveChannel(channel.Name)) continue;

			var userLeftRoster = channel.Part(session.Id);
			BroadcastChannelInfo(channel);
			if (!userLeftRoster) continue;

			if (userStillPresent)
			{
				BroadcastToOtherIrcMembers(channel, session.Id,
					IrcMessageWriter.Part(session.Name, session.Id, channel.Name));
			}
			else
			{
				foreach (var memberId in channel.MemberIds)
				{
					if (memberId == session.Id || !quitNotified.Add(memberId)) continue;
					if (ircRegistry.GetByUserId(memberId) is { } irc)
						irc.IrcConnection.Send(quitMessage);
				}
			}
		}
	}

	/// <summary>
	///     Builds the RPL_NAMREPLY and RPL_ENDOFNAMES numeric pair that reports a channel's member
	///     list, one entry per UserId regardless of how many of its sessions are present.
	/// </summary>
	/// <param name="requesterName">The nick the reply is addressed to.</param>
	/// <param name="channel">The channel whose members are listed.</param>
	/// <returns>The two numerics that form the channel's /NAMES reply.</returns>
	public IEnumerable<IrcMessage> BuildNamesReply(string requesterName, ChannelSession channel)
	{
		var names = channel.MemberIds
			.Select(id => (UserSession?)gameRegistry.GetByUserId(id) ?? ircRegistry.GetByUserId(id))
			.Where(member => member is not null)
			.Select(member => NamePrefix(member!) + member!.Name);

		yield return IrcMessageWriter.Numeric(options.Value.Name, IrcNumeric.RplNamReply, requesterName, "=",
			channel.Name, string.Join(' ', names));
		yield return IrcMessageWriter.Numeric(options.Value.Name, IrcNumeric.RplEndOfNames, requesterName,
			channel.Name, "End of /NAMES list");
	}

	/// <summary>
	///     Computes the IRC status prefix that precedes a channel member's nick in a /NAMES reply.
	/// </summary>
	/// <param name="member">The member whose prefix is computed.</param>
	/// <returns>The <c>@</c> moderator prefix, or an empty string for an unmarked member.</returns>
	private static string NamePrefix(UserSession member)
	{
		return (member.Privilege & UserPrivileges.Moderator) != 0 ? "@" : "";
	}

	/// <summary>
	///     Sends raw packet bytes to every <see cref="GameSession" /> currently in the channel, not
	///     everyone who merely can read it, optionally skipping the given immune set. Multiplayer
	///     routes match.enqueue and enqueue_state through the match's chat channel by calling this.
	/// </summary>
	/// <param name="channel">The channel whose members receive the packet.</param>
	/// <param name="packet">The raw bancho packet bytes to enqueue on each member's session.</param>
	/// <param name="immune">The set of member ids to skip, or null to broadcast to everyone.</param>
	public void BroadcastToMembers(ChannelSession channel, byte[] packet, IReadOnlyCollection<int>? immune = null)
	{
		foreach (var memberId in channel.MemberIds)
		{
			if (immune is not null && immune.Contains(memberId)) continue;
			if (gameRegistry.GetByUserId(memberId) is { } game)
				game.Enqueue(packet);
		}
	}

	/// <summary>
	///     The IRC-shaped counterpart of <see cref="BroadcastToMembers" /> for chat text specifically.
	///     Delivers to every session (game and IRC alike) of each member, so an account with both open
	///     sees channel chat on either.
	/// </summary>
	/// <param name="channel">The channel whose members receive the message.</param>
	/// <param name="message">The IRC-shaped message to deliver.</param>
	/// <param name="skipMemberId">The id of a member to skip, typically the message's sender, or null to deliver to everyone.</param>
	public void BroadcastPrivmsg(ChannelSession channel, IrcMessage message, int? skipMemberId = null)
	{
		foreach (var memberId in channel.MemberIds)
		{
			if (memberId == skipMemberId) continue;
			if (gameRegistry.GetByUserId(memberId) is { } game)
				game.IrcConnection.Send(message);
			if (ircRegistry.GetByUserId(memberId) is { } irc)
				irc.IrcConnection.Send(message);
		}
	}

	private void BroadcastToOtherIrcMembers(ChannelSession channel, int excludeUserId, IrcMessage message)
	{
		foreach (var memberId in channel.MemberIds)
		{
			if (memberId == excludeUserId) continue;
			if (ircRegistry.GetByUserId(memberId) is { } irc)
				irc.IrcConnection.Send(message);
		}
	}

	private void BroadcastChannelInfo(ChannelSession channel)
	{
		var packet = ServerPacketWriter.ChannelInfo(channel.DisplayName, channel.Topic, channel.PlayerCount);

		if (channel.Instance)
		{
			foreach (var memberId in channel.MemberIds)
				if (gameRegistry.GetByUserId(memberId) is { } game)
					game.Enqueue(packet);
		}
		else
		{
			foreach (var session in gameRegistry.All)
				if (channel.CanRead(session.Privilege))
					session.Enqueue(packet);
		}
	}
}
