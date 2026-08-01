using Basil.Application.Abstractions.Social;
using Basil.Application.Abstractions.Users;
using Basil.Application.Services.Bot;
using Basil.Application.Sessions;
using Basil.Application.Sessions.Channels;
using Basil.Application.Sessions.Multiplayer;
using Basil.Domain.Users;
using Basil.Protocol.Irc;
using Basil.Protocol.Packets;
using Microsoft.Extensions.Logging;

namespace Basil.Application.Services.Chat;

/// <summary>
///     Routes an outgoing chat message to a channel or a private recipient.
/// </summary>
/// <remarks>
///     This is the single entry point for "a sender said <c>text</c> to <c>channelOrNick</c>", used
///     identically by the bancho packet handlers
///     <see cref="Basil.Application.PacketHandlers.Channels.SendPublicMessageHandler" /> and
///     <see cref="Basil.Application.PacketHandlers.Channels.SendPrivateMessageHandler" /> and by a
///     real IRC connection's PRIVMSG. A leading <c>#</c> routes to the channel path, which broadcasts
///     and dispatches any <c>!</c> command; anything else resolves to a user, either the bot for a
///     command shortcut or a regular recipient whose message is checked against block, PM-privacy,
///     and silence state. Delivery is online only, with no offline persistence. The service lives one
///     layer above <see cref="ChannelMembershipService" /> specifically to avoid a dependency cycle:
///     it depends on <see cref="ICommandDispatcher" />, which chains back down through the match
///     services to <see cref="ChannelMembershipService" />, so that class must stay free of any
///     dependency on this one.
/// </remarks>
public sealed class ChatDispatchService(
	IChannelRegistry channelRegistry,
	IPlayerSessionRegistry sessionRegistry,
	ChannelMembershipService channelMembership,
	IUserRepository users,
	IRelationshipRepository relationships,
	ICommandDispatcher commandDispatcher,
	IMatchRegistry matchRegistry,
	ILogger<ChatDispatchService> logger)
{
	private const int MaxMessageLength = 2000;

	/// <summary>
	///     Dispatches a chat message from <paramref name="sender" /> to either a channel or a private
	///     recipient.
	/// </summary>
	/// <param name="sender">The player sending the message.</param>
	/// <param name="channelOrNick">
	///     The destination: a <c>#</c>-prefixed channel name, or the name of the receiving user.
	/// </param>
	/// <param name="text">The message body.</param>
	/// <param name="cancellationToken">The cancellation token to observe.</param>
	public async Task SendPrivmsgAsync(PlayerSession sender, string channelOrNick, string text,
		CancellationToken cancellationToken = default)
	{
		if (sender.Silenced)
		{
			logger.LogDebug("Message dropped: SenderId={SenderId} Reason=Silenced", sender.Id);
			return;
		}

		if (channelOrNick.StartsWith('#'))
		{
			await SendChannelMessageAsync(sender, channelOrNick, text, cancellationToken);
			return;
		}

		var target = sessionRegistry.GetByName(channelOrNick);
		if (target is { IsBot: true })
		{
			await SendBotCommandAsync(sender, target, text, cancellationToken);
			return;
		}

		await DeliverPrivateMessageAsync(sender, channelOrNick, target, text, cancellationToken);
	}

	/// <summary>
	///     Translates the fixed alias a bancho client uses for a match or spectator channel into the
	///     registry's internal channel name.
	/// </summary>
	/// <remarks>
	///     A bancho client only ever knows a match or spectator channel by its fixed alias
	///     (<c>#multiplayer</c> or <c>#spectator</c>), never the internal registry name
	///     (<see cref="MatchSession.ChatChannelName" />, for example <c>#multi_5</c>) that
	///     <see cref="IChannelRegistry.GetByName" /> actually indexes on. This mirrors the inverse
	///     translation done for outbound messages in <c>BanchoIrcBridgeConnection.TranslateRecipient</c>.
	/// </remarks>
	/// <param name="sender">The player sending the message.</param>
	/// <param name="channelName">The channel name as the client wrote it.</param>
	/// <returns>
	///     The internal channel name that indexes the registry, or the input unchanged when no
	///     translation applies.
	/// </returns>
	private static string ResolveClientChannelName(PlayerSession sender, string channelName)
	{
		if (channelName == "#multiplayer" && sender.Match is { } match) return match.ChatChannelName;

		if (channelName == "#spectator")
		{
			var hostId = sender.Spectating?.Id ?? (sender.Spectators.Count > 0 ? sender.Id : null);
			if (hostId is { } id) return $"#spec_{id}";
		}

		return channelName;
	}

	private async Task SendChannelMessageAsync(PlayerSession sender, string channelName, string text,
		CancellationToken cancellationToken)
	{
		var resolvedName = ResolveClientChannelName(sender, channelName);
		var channel = channelRegistry.GetByName(resolvedName);
		if (channel is null || !channel.Contains(sender.Id) || !channel.CanWrite(sender.Privilege))
		{
			logger.LogDebug("Message dropped: SenderId={SenderId} Reason=ChannelWriteDenied", sender.Id);
			return;
		}

		var truncated = text.Length > MaxMessageLength ? text[..MaxMessageLength] : text;

		channelMembership.BroadcastPrivmsg(
			channel, IrcMessageWriter.Privmsg(sender.Name, sender.Id, channel.Name, truncated),
			sender.Id);

		var bot = sessionRegistry.GetById(BotBootstrapService.BotId);
		if (bot is null) return;

		var matchScope = sender.Match is not null && sender.Match.ChatChannelName == channel.Name
			? sender.Match
			: null;
		var sink = new ChannelReplySink(channelMembership, channel, bot, sender);
		await commandDispatcher.DispatchAsync(sender, truncated, matchScope, channel.Name, sink,
			cancellationToken: cancellationToken);
	}

	private async Task SendBotCommandAsync(PlayerSession sender, PlayerSession bot, string text,
		CancellationToken cancellationToken)
	{
		var sink = new DmReplySink(sender, bot, channelMembership, channelRegistry, matchRegistry);
		await commandDispatcher.DispatchAsync(sender, text, null, null, sink, true, cancellationToken);
	}

	private async Task DeliverPrivateMessageAsync(PlayerSession sender, string recipientName, PlayerSession? target,
		string text, CancellationToken cancellationToken)
	{
		int targetId;
		if (target is not null)
		{
			targetId = target.Id;
		}
		else
		{
			var targetUser = await users.FetchByNameAsync(recipientName, cancellationToken);
			if (targetUser is null) return;

			targetId = targetUser.Id;
		}

		var relationship = await relationships.FetchOneAsync(targetId, sender.Id, cancellationToken);
		if (relationship?.Type == RelationshipType.Block)
		{
			logger.LogDebug("Message dropped: SenderId={SenderId} Reason=Blocked", sender.Id);
			sender.Enqueue(ServerPacketWriter.UserDmBlocked(recipientName));
			return;
		}

		if (target is not null)
		{
			if (target.PmPrivate && relationship?.Type != RelationshipType.Friend)
			{
				logger.LogDebug("Message dropped: SenderId={SenderId} Reason=PmPrivate", sender.Id);
				sender.Enqueue(ServerPacketWriter.UserDmBlocked(recipientName));
				return;
			}

			if (target.Silenced)
			{
				logger.LogDebug("Message dropped: SenderId={SenderId} Reason=TargetSilenced", sender.Id);
				sender.Enqueue(ServerPacketWriter.TargetSilenced(recipientName));
				return;
			}

			target.IrcConnection.Send(IrcMessageWriter.Privmsg(sender.Name, sender.Id, recipientName, text));

			if (target.Status.UserActivity == UserActivity.Afk && target.AwayMessage is { } awayMessage)
				sender.IrcConnection.Send(IrcMessageWriter.Privmsg(target.Name, target.Id, sender.Name, awayMessage));
		}
	}

	/// <summary>
	///     Routes a command's reply back into the channel the command was issued from.
	/// </summary>
	/// <remarks>
	///     Used for a command run inside a channel such as <c>#lobby</c> or a match's own chat.
	///     <see cref="Reply" /> broadcasts each line back into that same channel;
	///     <see cref="ReplyDm" />, used only by <c>!help</c> and <c>!mp help</c>, always goes to the
	///     sender's DM instead.
	/// </remarks>
	private sealed class ChannelReplySink(
		ChannelMembershipService membership,
		ChannelSession channel,
		PlayerSession bot,
		PlayerSession sender) : ICommandReplySink
	{
		/// <summary>Broadcasts a reply line into the source channel.</summary>
		/// <param name="text">The reply text to send.</param>
		public void Reply(string text)
		{
			// A reply may embed `\n` (e.g. !faq's file contents, !mp settings) — each line becomes its
			// own chat message, matching how a real client displays multiple consecutive lines rather
			// than one with a visible newline.
			foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
				membership.BroadcastPrivmsg(channel, IrcMessageWriter.Privmsg(bot.Name, bot.Id, channel.Name, line));
		}

		/// <summary>Sends a reply line to the sender's DM instead of the source channel.</summary>
		/// <param name="text">The reply text to send.</param>
		public void ReplyDm(string text)
		{
			foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
				sender.IrcConnection.Send(IrcMessageWriter.Privmsg(bot.Name, bot.Id, sender.Name, line));
		}
	}

	/// <summary>
	///     Routes a command run via DM to the bot back to the sender, scoped to the sender's current
	///     match.
	/// </summary>
	/// <remarks>
	///     <see cref="Reply" /> always DMs the sender, prefixed with <c>[#id]</c> once the sender is
	///     resolvably scoped to a match. Scope resolution mirrors
	///     <see cref="CommandDispatcher.ResolveScope" />'s precedence, re-checked per call because
	///     <c>!mp make</c> and <c>!mp in</c> establish that scope as part of the very reply being sent.
	///     When a match resolves, an unprefixed copy is also broadcast into the match's own channel, so
	///     referees running the room remotely stay visible to it. <see cref="ReplyDm" />, used for
	///     help, never prefixes or broadcasts.
	/// </remarks>
	private sealed class DmReplySink(
		PlayerSession sender,
		PlayerSession bot,
		ChannelMembershipService membership,
		IChannelRegistry channelRegistry,
		IMatchRegistry matchRegistry) : ICommandReplySink
	{
		/// <summary>
		///     Sends each reply line to the sender's DM, prefixed with the resolved match's
		///     <c>[#id]</c>, and broadcasts an unprefixed copy into that match's channel.
		/// </summary>
		/// <param name="text">The reply text to send.</param>
		public void Reply(string text)
		{
			var scope = ResolveScope();
			foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
			{
				var prefixed = scope is not null ? $"[#{scope.DbId}] {line}" : line;
				sender.IrcConnection.Send(IrcMessageWriter.Privmsg(bot.Name, bot.Id, sender.Name, prefixed));

				if (scope is null) continue;

				var channel = channelRegistry.GetByName(scope.ChatChannelName);
				if (channel is not null)
					membership.BroadcastPrivmsg(channel,
						IrcMessageWriter.Privmsg(bot.Name, bot.Id, channel.Name, line));
			}
		}

		/// <summary>Sends a reply line to the sender's DM with no prefix and no broadcast.</summary>
		/// <param name="text">The reply text to send.</param>
		public void ReplyDm(string text)
		{
			foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
				sender.IrcConnection.Send(IrcMessageWriter.Privmsg(bot.Name, bot.Id, sender.Name, line));
		}

		/// <summary>
		///     Resolves the sender's current match scope, preferring the out-of-room scope set by
		///     <c>!mp in</c> and falling back to the match the sender is physically in.
		/// </summary>
		/// <returns>The match the sender is scoped to, or <see langword="null" /> for none.</returns>
		private MatchSession? ResolveScope()
		{
			if (sender.MpScopeMatchId is { } dbId)
			{
				var scoped = matchRegistry.GetByDbId(dbId);
				if (scoped is not null) return scoped;
			}

			return sender.Match;
		}
	}
}