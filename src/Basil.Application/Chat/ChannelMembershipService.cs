using System.Text.Json;
using Basil.Application.Bot;
using Basil.Application.Channels;
using Basil.Application.Irc;
using Basil.Application.Multiplayer;
using Basil.Application.Sessions;
using Basil.Application.Shared.Eventing;
using Basil.Application.Shared.Json;
using Basil.Application.Users;
using Basil.Domain.Users;
using Microsoft.Extensions.Options;

namespace Basil.Application.Chat;

/// <summary>
///     Handles the shared join and part workflow for channels. This service is used by both
///     client-initiated CHANNEL_JOIN/CHANNEL_PART packets and server-managed memberships such as
///     spectator channels.
///     Broadcast behavior depends on the channel type. Instance channels notify only their current
///     members, while ordinary channels notify every <see cref="GameSession" /> with read access.
///     This class also provides the low-level IRC JOIN, PART, and PRIVMSG broadcast primitives.
///     It intentionally does not depend on <c>ICommandDispatcher</c>; command dispatch belongs to
///     <c>ChatDispatchService</c>, and introducing that dependency here would create a cycle
///     (CommandDispatcher → MpCommandService → MatchMembership → this class).
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
	IChatNotifier chat,
	IChannelNotifier channels,
	IMatchRegistry matchRegistry,
	ILiveEventHub hub,
	IOptions<IrcSettings> options,
	IUserCache userCache)
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
	///     or currently seated in it — the same rule <see cref="Listable" /> uses to decide what a room
	///     shows up as. This is a no-op for every other kind of channel.
	/// </remarks>
	public bool Join(UserSession userSession, ChannelSession channel, bool bypassMatchGate = false)
	{
		if (!bypassMatchGate && !CanJoinMatchChannel(userSession, channel)) return false;
		if (!channel.CanRead(userSession.Privilege)) return false;
		if (!userSession.JoinChannel(channel.Name)) return false;

		var userEnteredRoster = channel.Join(userSession.Id);

		channels.Joined(userSession, channel, Roster(channel));

		if (userEnteredRoster)
		{
			channels.RosterChanged(channel);
			channels.MemberJoined(channel, userSession);
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

		channels.Left(userSession, channel, kick);

		if (!userLeftRoster) return;

		channels.RosterChanged(channel);
		channels.MemberLeft(channel, userSession);
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
		var quitNotified = new HashSet<int>();

		foreach (var channelName in session.Channels.ToArray())
		{
			if (channelRegistry.GetByName(channelName) is not { } channel) continue;
			if (!session.LeaveChannel(channel.Name)) continue;

			var userLeftRoster = channel.Part(session.Id);
			channels.RosterChanged(channel);
			if (!userLeftRoster) continue;

			if (userStillPresent)
				channels.MemberLeft(channel, session);
			else
				foreach (var memberId in channel.MemberIds)
					if (memberId != session.Id)
						quitNotified.Add(memberId);
		}

		if (!userStillPresent)
			channels.Quit(session, quitNotified, quitReason);
	}

	/// <summary>
	///     Gets a channel's current member list, one prefixed name per UserId regardless of how many
	///     of its sessions are present, in roster order.
	/// </summary>
	/// <param name="channel">The channel whose roster is read.</param>
	/// <returns>The channel's member names, each prefixed by <see cref="MemberPrefix" />.</returns>
	public IReadOnlyList<string> Roster(ChannelSession channel)
	{
		return channel.MemberIds
			.Select(id => (UserSession?)gameRegistry.GetByUserId(id) ?? ircRegistry.GetByUserId(id))
			.Where(member => member is not null)
			.Select(member => MemberPrefix(member!, channel) + member!.Name)
			.ToList();
	}

	/// <summary>
	///     Gets the channels a requester may be listed, filtered to a matching name when one is given.
	/// </summary>
	/// <param name="requester">The session whose privilege decides what is visible.</param>
	/// <param name="channelFilter">
	///     An optional comma-separated list of channel names to restrict the listing to. A value that
	///     is not a channel name, such as a client-sent server or member-count mask, lists everything
	///     visible instead.
	/// </param>
	/// <returns>The channels the requester may be shown, in wire order.</returns>
	public IEnumerable<ChannelSession> Listable(UserSession requester, string? channelFilter = null)
	{
		var filter = channelFilter is not null && channelFilter.StartsWith('#')
			? channelFilter.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
			: null;

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

			yield return channel;
		}
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
			? match.IsReferee(userCache.Resolve(member))
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
		var user = userCache.Resolve(userSession);
		return match.IsReferee(user) || match.GetSlot(user) is not null;
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
	///     Delivers one line of chat to every session (game and IRC alike) of each member of a
	///     channel, so an account with both open sees channel chat on either.
	/// </summary>
	/// <param name="channel">The channel whose members receive the line.</param>
	/// <param name="line">The line to deliver.</param>
	/// <param name="skipMemberId">The id of a member to skip, typically the line's sender, or null to deliver to everyone.</param>
	public void BroadcastPrivmsg(ChannelSession channel, ChatLine line, int? skipMemberId = null)
	{
		foreach (var memberId in channel.MemberIds)
		{
			if (memberId == skipMemberId) continue;
			if (gameRegistry.GetByUserId(memberId) is { } game) chat.Deliver(game, line);
			if (ircRegistry.GetByUserId(memberId) is { } irc) chat.Deliver(irc, line);
		}

		PublishMatchChat(channel, line.SenderId, line.SenderName, line.Text);
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

		channels.RosterChanged(channel);

		if (gameRegistry.GetByUserId(BotBootstrapService.BotId) is not { } bot) return;
		channels.TopicChanged(channel, bot, topic);
	}

	/// <summary>
	///     Publishes a line said in a match's own channel to that match's live chat stream. Published
	///     outside the delivery loop above, so an observer sees the sender's own line too.
	/// </summary>
	private void PublishMatchChat(ChannelSession channel, int senderId, string senderName, string text)
	{
		if (MatchFor(channel) is not { } match) return;

		var session = (UserSession?)gameRegistry.GetByUserId(senderId) ?? ircRegistry.GetByUserId(senderId);
		var sender = new UserBrief(senderId, session?.Name ?? senderName, session?.Country ?? Country.Xx);
		var chatMessage = new MatchChatMessage(sender, text, DateTimeOffset.UtcNow);

		hub.Publish(MatchStreams.Chat(match.DbId), match.AllocateChatVersion(),
			JsonSerializer.SerializeToUtf8Bytes(chatMessage, BasilJsonOptions.Instance));
	}
}