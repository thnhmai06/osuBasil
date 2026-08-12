using System.Globalization;
using System.Text.Json;
using Basil.Application.Configurations;
using Basil.Application.Formats;
using Basil.Application.Services.Bot;
using Basil.Application.Services.Irc;
using Basil.Application.Services.Multiplayer;
using Basil.Application.Sessions.Multiplayer;
using Basil.Domain.Login;
using Basil.Domain.Users;
using Basil.Protocol.Irc;
using Basil.Protocol.Packets;
using Microsoft.Extensions.Options;

namespace Basil.Application.Sessions.Channels;

/// <summary>
///     Handles the shared join and part workflow for channels. This service is used by both
///     client-initiated CHANNEL_JOIN/CHANNEL_PART packets and server-managed memberships such as
///     spectator channels.
///     Broadcast behavior depends on the channel type. Instance channels notify only their current
///     members, while ordinary channels notify every <see cref="GameSession" /> with read access.
///     This class also provides the low-level IRC JOIN, PART, and PRIVMSG broadcast primitives.
///     It intentionally does not depend on <c>ICommandDispatcher</c>; command dispatch belongs to
///     <c>ChatDispatchService</c>, and introducing that dependency here would create a cycle
///     (CommandDispatcher → MpCommandService → MatchMembershipService → this class).
/// </summary>
/// <remarks>
///     A single account may own both a <see cref="GameSession" /> and an
///     <see cref="IrcSession" /> simultaneously. Channel membership is tracked per session through
///     <see cref="UserSession.JoinChannel" /> and <see cref="UserSession.LeaveChannel" />, while the
///     channel roster (<see cref="ChannelSession.MemberIds" />) stores each UserId only once,
///     regardless of how many sessions are present. <see cref="ChannelSession.Join" /> and
///     <see cref="ChannelSession.Part" /> indicate whether the roster actually changed.
///     Only roster changes are broadcast to other members. Every joining or parting session still
///     receives its own confirmation, so additional sessions for an already-present UserId are
///     acknowledged locally without generating redundant JOIN or PART broadcasts.
/// </remarks>
public sealed class ChannelMembershipService(
	ISessionRegistry<GameSession> gameRegistry,
	ISessionRegistry<IrcSession> ircRegistry,
	IChannelRegistry channelRegistry,
	IMatchRegistry matchRegistry,
	IMatchLiveEvents matchLiveEvents,
	IOptions<IrcOptions> options)
{
	/// <summary>
	///     Adds a user session to a channel, echoing the join to the session itself and, only when this
	///     is the first session of its UserId present, broadcasting to the channel's other members.
	/// </summary>
	/// <param name="userSession">The session of the user joining the channel.</param>
	/// <param name="channel">The channel to join.</param>
	/// <param name="bypassMatchGate">
	///     Skips the default multiplayer channel membership gate for trusted internal callers
	///     that have already performed equivalent authorization or are joining before the
	///     match state is fully established.
	///     <list type="bullet">
	///         <item>
	///             <description>
	///                 Occupying a slot before the slot assignment has been committed.
	///             </description>
	///         </item>
	///         <item>
	///             <description>
	///                 Joining the match creator before referee assignment has completed.
	///             </description>
	///         </item>
	///         <item>
	///             <description>
	///                 Tournament spectator sessions authorized by their own privilege checks.
	///             </description>
	///         </item>
	///         <item>
	///             <description>
	///                 Chat-only joins whose privacy, password, and ban checks have already been validated.
	///             </description>
	///         </item>
	///     </list>
	/// </param>
	/// <returns><see langword="true" /> if the user session was added to the channel; otherwise, <see langword="false" />.</returns>
	/// <remarks>
	///     A match room's channel additionally requires the userSession to be a referee of that match
	///     or currently seated in it — the same rule <see cref="BuildListReply" /> uses to decide what
	///     a room shows up as. This is a no-op for every other kind of channel.
	/// </remarks>
	public bool Join(UserSession userSession, ChannelSession channel, bool bypassMatchGate = false)
	{
		if (!bypassMatchGate && !CanJoinMatchChannel(userSession, channel)) return false;
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
				BroadcastToOtherIrcMembers(channel, session.Id,
					IrcMessageWriter.Part(session.Name, session.Id, channel.Name));
			else
				foreach (var memberId in channel.MemberIds)
				{
					if (memberId == session.Id || !quitNotified.Add(memberId)) continue;
					if (ircRegistry.GetByUserId(memberId) is { } irc)
						irc.IrcConnection.Send(quitMessage);
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
			.Select(member => MemberPrefix(member!, channel) + member!.Name);

		yield return IrcMessageWriter.Numeric(options.Value.Name, IrcNumeric.RplNamReply, requesterName, "=",
			channel.Name, string.Join(' ', names));
		yield return IrcMessageWriter.Numeric(options.Value.Name, IrcNumeric.RplEndOfNames, requesterName,
			channel.Name, IrcReplies.EndOfNames);
	}

	/// <summary>
	///     Builds the RPL_LISTSTART, RPL_LIST, and RPL_LISTEND numerics that report the channels the
	///     requester may read, each with its current member count and topic.
	/// </summary>
	/// <param name="requester">The session the reply is addressed to, whose privilege decides what is visible.</param>
	/// <param name="channelFilter">
	///     An optional comma-separated list of channel names to restrict the listing to. A value that
	///     is not a channel name, such as a client-sent server or member-count mask, lists everything
	///     visible instead.
	/// </param>
	/// <returns>The numerics that form the /LIST reply in wire order.</returns>
	public IEnumerable<IrcMessage> BuildListReply(UserSession requester, string? channelFilter = null)
	{
		var filter = channelFilter is not null && channelFilter.StartsWith('#')
			? channelFilter.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
			: null;

		yield return IrcMessageWriter.Numeric(options.Value.Name, IrcNumeric.RplListStart, requester.Name,
			IrcReplies.ListChannel, IrcReplies.ListUsers);

		foreach (var channel in channelRegistry.All.OrderBy(channel => channel.Name, StringComparer.Ordinal))
		{
			// A non-match instance channel (spectator) never lists — it has no referee/participant
			// concept to gate on, so it stays a pure "you already know the name" channel.
			if (channel.Instance && MatchFor(channel) is null) continue;
			// A match room lists only for its own participants and referees — the same rule JOIN
			// enforces — so it's never advertised to, or joinable by, anyone else.
			if (!CanJoinMatchChannel(requester, channel)) continue;
			if (!channel.CanRead(requester.Privilege)) continue;
			if (filter is not null && !filter.Contains(channel.Name, StringComparer.OrdinalIgnoreCase)) continue;

			yield return IrcMessageWriter.Numeric(options.Value.Name, IrcNumeric.RplList, requester.Name,
				channel.Name, channel.PlayerCount.ToString(CultureInfo.InvariantCulture), channel.Topic);
		}

		yield return IrcMessageWriter.Numeric(options.Value.Name, IrcNumeric.RplListEnd, requester.Name,
			IrcReplies.EndOfList);
	}

	/// <summary>
	///     Computes the IRC status prefix that marks a member's standing in one channel: <c>@</c> for
	///     whoever holds authority over it, <c>+</c> for a member who may write to it, and none for a
	///     member who may only read.
	/// </summary>
	/// <remarks>
	///     Authority is per channel, not global: inside a match room it belongs to that room's
	///     referees, and everywhere else to staff. A referee is therefore unmarked in an ordinary
	///     channel, and a staff member holds no operator status in a room they are not refereeing.
	/// </remarks>
	/// <param name="member">The member whose prefix is computed.</param>
	/// <param name="channel">The channel the member's standing is judged in.</param>
	/// <returns>The <c>@</c>, <c>+</c>, or empty prefix for the member.</returns>
	public string MemberPrefix(UserSession member, ChannelSession channel)
	{
		var authority = MatchFor(channel) is { } match
			? match.IsReferee(member.Id)
			: (member.Privilege & UserPrivileges.Staff) != 0;

		if (authority) return "@";

		return channel.CanWrite(member.Privilege) ? "+" : "";
	}

	/// <summary>Gets the match that owns <paramref name="channel" />, or null when no match does.</summary>
	private MatchSession? MatchFor(ChannelSession channel)
	{
		// Only an instance channel is ever owned by a match, and a tournament server holds a handful
		// of live rooms at a time — scanning them beats maintaining a name index nothing else needs.
		return channel.Instance
			? matchRegistry.All.FirstOrDefault(match => match.ChatChannelName == channel.Name)
			: null;
	}

	/// <summary>
	///     Reports whether a user may join or be listed in a match room's channel: a referee of
	///     that match or a user currently seated in it. A channel that is not a match room
	///     always passes.
	/// </summary>
	/// <param name="userSession">The userSession being checked.</param>
	/// <param name="channel">The channel being joined or listed.</param>
	private bool CanJoinMatchChannel(UserSession userSession, ChannelSession channel)
	{
		if (MatchFor(channel) is not { } match) return true;
		return match.IsReferee(userSession.Id) || match.GetSlot(userSession.Id) is not null;
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

		PublishMatchChat(channel, message);
	}

	/// <summary>
	///     Updates a channel's topic and pushes the change to every current member — a fresh
	///     <c>ChannelInfo</c> for bancho clients (whose channel-list entry already carries the topic)
	///     and a <c>TOPIC</c> line, attributed to BasilBot, for real IRC clients.
	/// </summary>
	/// <remarks>
	///     A no-op when <paramref name="topic" /> already matches the channel's current topic, so a
	///     caller resyncing on every settings broadcast doesn't spam a <c>TOPIC</c> line for every
	///     unrelated room-setting change.
	/// </remarks>
	/// <param name="channel">The channel whose topic to update.</param>
	/// <param name="topic">The new topic text.</param>
	public void SyncTopic(ChannelSession channel, string topic)
	{
		if (channel.Topic == topic) return;
		channel.Topic = topic;

		BroadcastChannelInfo(channel);

		if (gameRegistry.GetByUserId(BotBootstrapService.BotId) is not { } bot) return;
		var message = IrcMessageWriter.Topic(bot.Name, bot.Id, channel.Name, topic);
		foreach (var memberId in channel.MemberIds)
			if (ircRegistry.GetByUserId(memberId) is { } irc)
				irc.IrcConnection.Send(message);
	}

	/// <summary>
	///     Publishes a line said in a match's own channel to that match's live chat stream. Published
	///     outside the delivery loop above, so an observer sees the sender's own line too.
	/// </summary>
	private void PublishMatchChat(ChannelSession channel, IrcMessage message)
	{
		if (message.Params.Count < 2) return;
		if (MatchFor(channel) is not { } match) return;
		if (!IrcMessageWriter.TryParseUserPrefix(message.Prefix, out var senderName, out var senderId)) return;

		var session = (UserSession?)gameRegistry.GetByUserId(senderId) ?? ircRegistry.GetByUserId(senderId);
		var sender = new UserBrief(senderId, session?.Name ?? senderName, session?.Country ?? Country.Xx);
		var chat = new MatchChatMessage(sender, message.Params[1], DateTimeOffset.UtcNow);

		matchLiveEvents.PublishChat(match.DbId,
			JsonSerializer.SerializeToUtf8Bytes(chat, BasilJsonOptions.Instance));
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