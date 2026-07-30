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
///     Single entry point for "a sender said `text` to `channelOrNick`" — used identically by bancho's
///     SendPublicMessage/SendPrivateMessage handlers and by a real IRC connection's PRIVMSG. A leading
///     '#' routes to the channel path (broadcast + `!`-command dispatch); anything else resolves to a
///     user (bot command shortcut, or block/away/silence-checked delivery — online only, no offline
///     persistence).
///     Lives one layer above <see cref="ChannelMembershipService" /> specifically to avoid a DI cycle:
///     this depends on <see cref="ICommandDispatcher" />, which chains back down to
///     <c>MatchMembershipService</c> -&gt; <see cref="ChannelMembershipService" /> — that class itself
///     must stay free of any dependency on this one.
/// </summary>
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
	///     A bancho client only ever knows a match/spectator channel by its fixed alias
	///     (<c>#multiplayer</c>/<c>#spectator</c>) — never the internal registry name
	///     (<see cref="MatchSession.ChatChannelName" />, e.g. <c>#multi_5</c>) that
	///     <see cref="IChannelRegistry.GetByName" /> actually indexes on. Mirrors the inverse
	///     translation in <c>BanchoIrcBridgeConnection.TranslateRecipient</c> (outbound direction).
	/// </summary>
	private static string ResolveClientChannelName(PlayerSession sender, string channelName)
	{
		if (channelName == "#multiplayer" && sender.Match is { } match) return match.ChatChannelName;

		if (channelName == "#spectator")
		{
			var hostId = sender.Spectating?.Id ?? (sender.Spectators.Count > 0 ? sender.Id : (int?)null);
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
	///     Reply sink for a command run from inside a channel (<c>#lobby</c>, a match's own chat, ...):
	///     <see cref="Reply" /> broadcasts back into that same channel; <see cref="ReplyDm" /> (used only
	///     by `!help`/`!mp help`) always goes to the sender's DM instead.
	/// </summary>
	private sealed class ChannelReplySink(
		ChannelMembershipService membership,
		ChannelSession channel,
		PlayerSession bot,
		PlayerSession sender) : ICommandReplySink
	{
		public void Reply(string text)
		{
			// A reply may embed `\n` (e.g. !faq's file contents, !mp settings) — each line becomes its
			// own chat message, matching how a real client displays multiple consecutive lines rather
			// than one with a visible newline.
			foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
				membership.BroadcastPrivmsg(channel, IrcMessageWriter.Privmsg(bot.Name, bot.Id, channel.Name, line));
		}

		public void ReplyDm(string text)
		{
			foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
				sender.IrcConnection.Send(IrcMessageWriter.Privmsg(bot.Name, bot.Id, sender.Name, line));
		}
	}

	/// <summary>
	///     Reply sink for a command run via DM to the bot: <see cref="Reply" /> always DMs the sender,
	///     prefixed with <c>[#id]</c> once the sender is resolvably scoped to a match (mirroring
	///     <see cref="Bot.CommandDispatcher.ResolveScope" />'s own MpScopeMatchId-then-physical-match
	///     precedence, re-checked per call since `!mp make`/`!mp in` only establish that scope as part of
	///     the very reply being sent) — and additionally broadcasts an unprefixed copy into that match's
	///     own channel, so referees running the room remotely stay visible to it. <see cref="ReplyDm" />
	///     (help) never prefixes or broadcasts.
	/// </summary>
	private sealed class DmReplySink(
		PlayerSession sender,
		PlayerSession bot,
		ChannelMembershipService membership,
		IChannelRegistry channelRegistry,
		IMatchRegistry matchRegistry) : ICommandReplySink
	{
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
					membership.BroadcastPrivmsg(channel, IrcMessageWriter.Privmsg(bot.Name, bot.Id, channel.Name, line));
			}
		}

		public void ReplyDm(string text)
		{
			foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
				sender.IrcConnection.Send(IrcMessageWriter.Privmsg(bot.Name, bot.Id, sender.Name, line));
		}

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
