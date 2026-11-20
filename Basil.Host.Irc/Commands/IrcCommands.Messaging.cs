using Basil.Application.Chat.Messages;
using Basil.Application.Chat.Objects;
using Basil.Application.Shared.Settings;
using Basil.Application.Users.Messages;
using Basil.Application.Users.Objects;
using Basil.Application.Users.Objects.Sessions;
using Basil.Domain.Chat;
using Basil.Host.Irc.Clients;
using Basil.Host.Irc.Support;
using Basil.Protocol.Irc.Models;

namespace Basil.Host.Irc.Commands;

internal sealed partial class IrcCommands
{
	private async Task MessageAsync(IrcSocket session, Session actor, IrcMessage message,
		CancellationToken ct)
	{
		var notice = message.Command == "NOTICE";
		if (message.Parameters.Count == 0)
		{
			session.Numeric(IrcNumericReply.ErrNoRecipient, $"No recipient given ({message.Command})");
			return;
		}

		if (message.Parameters.Count < 2 || message.Parameters[1].Length == 0)
		{
			session.Numeric(IrcNumericReply.ErrNoTextToSend, "No text to send");
			return;
		}

		var target = message.Parameters[0];
		ChatTarget destination;
		if (target.StartsWith('#'))
		{
			if (target.StartsWith("#spec_", StringComparison.OrdinalIgnoreCase))
			{
				session.Numeric(IrcNumericReply.ErrNoSuchChannel, target, IrcReplies.NoSuchChannel);
				return;
			}

			destination = RoomChannelRules.TryParse(target, out var roomId)
				? new ChatTarget.Room(roomId)
				: new ChatTarget.General(target);
		}
		else
		{
			if (FindUser(target) is not { } user)
			{
				session.Numeric(IrcNumericReply.ErrNoSuchNick, target, IrcReplies.NoSuchNick);
				return;
			}

			destination = new ChatTarget.Private(user.Record.Id);
		}

		if (!await MayChatAsync(session, actor, message.Command, target, ct)) return;

		try
		{
			var result = await sender.Send(
				new PostChatMessage(actor, destination, IrcText.Limit(message.Parameters[1]), notice), ct);
			ReplyToPost(session, target, result);
		}
		catch (EmptyMessageException)
		{
			session.Numeric(IrcNumericReply.ErrNoTextToSend, "No text to send");
		}
	}

	private async Task<bool> MayChatAsync(IrcSocket session, Session actor, string command, string target,
		CancellationToken ct)
	{
		var limit = (await sender.Send(new ReadSettings(), ct)).RateLimits.Chat;
		if (limiter.TryAcquire($"chat:{actor.Record.Id}", limit.Count, limit.Window)) return true;

		if (session.Has("standard-replies"))
			session.Send(new IrcMessage("FAIL", session.ServerName, command, IrcReplies.RateLimitedCode, target,
				IrcReplies.ChatTooFast));
		else
			session.Send(new IrcMessage("NOTICE", session.ServerName, session.Nick, IrcReplies.ChatTooFast));
		return false;
	}

	private static void ReplyToPost(IrcSocket session, string target, ChannelJoinResult result)
	{
		if (result == ChannelJoinResult.Denied)
		{
			session.Numeric(IrcNumericReply.ErrCannotSendToChannel, target, IrcReplies.Silenced);
			return;
		}

		switch (result)
		{
			case ChannelJoinResult.Closed or ChannelJoinResult.NotFound:
				session.Numeric(IrcNumericReply.ErrNoSuchChannel, target, IrcReplies.NoSuchChannel);
				break;
			case ChannelJoinResult.NotMember or ChannelJoinResult.AccessDenied:
				session.Numeric(IrcNumericReply.ErrCannotSendToChannel, target, IrcReplies.CannotSend);
				break;
			case ChannelJoinResult.Blocked:
				session.Numeric(IrcNumericReply.ErrCannotSendToChannel, target, IrcReplies.DmBlocked);
				break;
			case ChannelJoinResult.TargetSilenced:
				session.Numeric(IrcNumericReply.ErrCannotSendToChannel, target, IrcReplies.TargetSilenced);
				break;
		}
	}

	private Channel? Resolve(string name)
	{
		return name.StartsWith("#spec_", StringComparison.OrdinalIgnoreCase) ? null : server.FindChannel(name);
	}

	private User? FindUser(string nick)
	{
		return registry.Online.Values.FirstOrDefault(user => IrcView.IsNick(user.Record, nick));
	}

	private async Task JoinAsync(IrcSocket session, Session actor, IrcMessage message,
		CancellationToken ct)
	{
		if (message.Parameters.Count == 0)
		{
			NeedMore(session, "JOIN");
			return;
		}

		foreach (var name in message.Parameters[0].Split(',', StringSplitOptions.RemoveEmptyEntries))
		{
			if (name.StartsWith("#spec_", StringComparison.OrdinalIgnoreCase))
			{
				session.Numeric(IrcNumericReply.ErrInviteOnlyChan, name, IrcReplies.CannotJoin);
				continue;
			}

			var result = await sender.Send(new JoinChannel(actor, IrcTarget(name)), ct);
			switch (result)
			{
				case ChannelJoinResult.Closed or ChannelJoinResult.NotFound:
					session.Numeric(IrcNumericReply.ErrNoSuchChannel, name, IrcReplies.NoSuchChannel);
					break;
				case ChannelJoinResult.AccessDenied:
					session.Numeric(IrcNumericReply.ErrInviteOnlyChan, name, IrcReplies.CannotJoin);
					break;
			}
		}
	}

	private async Task PartAsync(IrcSocket session, Session actor, IrcMessage message,
		CancellationToken ct)
	{
		if (message.Parameters.Count == 0)
		{
			NeedMore(session, "PART");
			return;
		}

		foreach (var name in message.Parameters[0].Split(',', StringSplitOptions.RemoveEmptyEntries))
		{
			var result = await sender.Send(new PartChannel(actor, IrcTarget(name)), ct);
			if (result == ChannelJoinResult.NotFound)
				session.Numeric(IrcNumericReply.ErrNoSuchChannel, name, IrcReplies.NoSuchChannel);
			else if (result == ChannelJoinResult.NotMember)
				session.Numeric(IrcNumericReply.ErrNotOnChannel, name, IrcReplies.NotOnChannel);
		}
	}

	private static ChatTarget IrcTarget(string name)
	{
		return RoomChannelRules.TryParse(name, out var roomId)
			? new ChatTarget.Room(roomId)
			: new ChatTarget.General(name);
	}

	private async Task AwayAsync(IrcSocket session, Session actor, IrcMessage message,
		CancellationToken ct)
	{
		var text = message.Parameters.Count > 0 && message.Parameters[0].Length > 0 ? message.Parameters[0] : null;
		await sender.Send(new SetAwayMessage(actor, text), ct);
		session.Numeric(text is null ? IrcNumericReply.RplUnaway : IrcNumericReply.RplNowAway,
			text is null ? "You are no longer marked as being away" : "You have been marked as being away");

		var line = new IrcMessage("AWAY", IrcView.Mask(actor.Record, Options.Domain).ToString(),
			text is null ? [] : [text]) { ForceTrailing = true };
		var mine = presence.Of(actor.Record);
		foreach (var other in clients.All.Where(other =>
			         other.Has("away-notify") && other.Session is { } connection &&
			         !ReferenceEquals(connection.User, actor.User) &&
			         mine.Any(channel => channel.Members.Contains(connection))))
			other.Deliver(line);
	}

	private void Management(IrcSocket session, IrcMessage message)
	{
		if (message.Parameters.Count < 2)
		{
			NeedMore(session, message.Command);
			return;
		}

		var channel = message.Command == "KICK" ? message.Parameters[0] : message.Parameters[1];
		if (!channel.StartsWith('#') || Resolve(channel) is null)
			session.Numeric(IrcNumericReply.ErrNoSuchChannel, channel, IrcReplies.NoSuchChannel);
		else
			session.Numeric(IrcNumericReply.ErrChanOPrivsNeeded, channel, IrcReplies.NoManagement);
	}

	private void Topic(IrcSocket session, Session actor, IrcMessage message)
	{
		if (message.Parameters.Count == 0)
		{
			NeedMore(session, "TOPIC");
			return;
		}

		var name = message.Parameters[0];
		if (Resolve(name) is not { } channel)
		{
			session.Numeric(IrcNumericReply.ErrNoSuchChannel, name, IrcReplies.NoSuchChannel);
			return;
		}

		if (message.Parameters.Count > 1)
			session.Numeric(IrcNumericReply.ErrChanOPrivsNeeded, channel.Name, IrcReplies.NoManagement);
		else if (!channel.Members.Contains(actor))
			session.Numeric(IrcNumericReply.ErrNotOnChannel, channel.Name, IrcReplies.NotOnChannel);
		else
			IrcChannels.SendTopic(session, channel);
	}

	private void Mode(IrcSocket session, Session actor, IrcMessage message)
	{
		if (message.Parameters.Count == 0)
		{
			NeedMore(session, "MODE");
			return;
		}

		var target = message.Parameters[0];
		if (target.StartsWith('#'))
		{
			if (Resolve(target) is not { } channel)
				session.Numeric(IrcNumericReply.ErrNoSuchChannel, target, IrcReplies.NoSuchChannel);
			else if (message.Parameters.Count == 1)
				session.NumericPlain(IrcNumericReply.RplChannelModeIs, channel.Name, "+");
			else
				session.Numeric(IrcNumericReply.ErrChanOPrivsNeeded, channel.Name, IrcReplies.NoManagement);
		}
		else if (!IrcView.IsNick(actor.Record, target))
		{
			session.Numeric(IrcNumericReply.ErrUsersDontMatch, "Cannot change mode for other users");
		}
		else if (message.Parameters.Count == 1)
		{
			session.NumericPlain(IrcNumericReply.RplUModeIs, "+");
		}
		else
		{
			session.Numeric(IrcNumericReply.ErrUModeUnknownFlag, "Unknown MODE flag");
		}
	}
}