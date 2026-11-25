using Basil.Gateway.RateLimiting;
using Basil.Application.Chat.Messages;
using Basil.Application.Chat.Objects;
using Basil.Application.Shared.Settings;
using Basil.Application.Users.Messages;
using Basil.Application.Users.Objects;
using Basil.Application.Sessions.Objects;
using Basil.Domain.Chat;
using Basil.Host.Irc.Clients;
using Basil.Host.Irc.Support;
using Basil.Protocol.Irc;
using Basil.Protocol.Irc.Models;

namespace Basil.Host.Irc.Commands;

internal sealed partial class IrcCommands
{
	private async Task MessageAsync(IrcSocket session, Session actor, IrcMessage message,
		CancellationToken ct)
	{
		var notice = message.Command == IrcVerb.Notice;
		if (message.Parameters.Count == 0)
		{
			session.Numeric(IrcNumericReply.ErrNoRecipient, replies.Text(IrcReply.NoRecipient, new { command = message.Command }));
			return;
		}

		if (message.Parameters.Count < 2 || message.Parameters[1].Length == 0)
		{
			session.Numeric(IrcNumericReply.ErrNoTextToSend, replies.Text(IrcReply.NoTextToSend));
			return;
		}

		var action = CtcpAction.TryRead(message.Parameters[1], out var body);
		var target = message.Parameters[0];
		ChatTarget destination;
		if (target.StartsWith('#'))
		{
			if (target.StartsWith("#spec_", StringComparison.OrdinalIgnoreCase))
			{
				session.Numeric(IrcNumericReply.ErrNoSuchChannel, target, replies.Text(IrcReply.NoSuchChannel));
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
				session.Numeric(IrcNumericReply.ErrNoSuchNick, target, replies.Text(IrcReply.NoSuchNick));
				return;
			}

			destination = new ChatTarget.Private(user.Record.Id);
		}

		if (!await MayChatAsync(session, actor, message.Command, target, ct)) return;

		try
		{
			var result = await sender.Send(
				new PostChatMessage(actor, destination, IrcText.Limit(body), notice, action), ct);
			ReplyToPost(session, target, result);
		}
		catch (EmptyMessageException)
		{
			session.Numeric(IrcNumericReply.ErrNoTextToSend, replies.Text(IrcReply.NoTextToSend));
		}
	}

	private async Task<bool> MayChatAsync(IrcSocket session, Session actor, string command, string target,
		CancellationToken ct)
	{
		if (admission.Admit(RateRule.Chat, actor).Allowed) return true;

		if (session.Has("standard-replies"))
			session.Send(IrcMessage.Fail(session.ServerName, command, IrcReplies.RateLimitedCode, target,
				replies.Text(IrcReply.ChatTooFast)));
		else
			session.Send(IrcMessage.ServerNotice(session.ServerName, session.Nick, replies.Text(IrcReply.ChatTooFast)));
		return false;
	}

	private void ReplyToPost(IrcSocket session, string target, ChannelJoinResult result)
	{
		if (result == ChannelJoinResult.Denied)
		{
			session.Numeric(IrcNumericReply.ErrCannotSendToChannel, target, replies.Text(IrcReply.Silenced));
			return;
		}

		switch (result)
		{
			case ChannelJoinResult.Closed or ChannelJoinResult.NotFound:
				session.Numeric(IrcNumericReply.ErrNoSuchChannel, target, replies.Text(IrcReply.NoSuchChannel));
				break;
			case ChannelJoinResult.NotMember:
				session.Numeric(IrcNumericReply.ErrCannotSendToChannel, target, replies.Text(IrcReply.CannotSendNotMember));
				break;
			case ChannelJoinResult.AccessDenied:
				session.Numeric(IrcNumericReply.ErrCannotSendToChannel, target, replies.Text(IrcReply.CannotSendNoAccess));
				break;
			case ChannelJoinResult.Blocked:
				session.Numeric(IrcNumericReply.ErrCannotSendToChannel, target, replies.Text(IrcReply.DmBlocked));
				break;
			case ChannelJoinResult.Silenced:
				session.Numeric(IrcNumericReply.ErrCannotSendToChannel, target, replies.Text(IrcReply.Silenced));
				break;
			case ChannelJoinResult.TargetSilenced:
				session.Numeric(IrcNumericReply.ErrCannotSendToChannel, target, replies.Text(IrcReply.TargetSilenced));
				break;
		}
	}

	private Channel? Resolve(string name)
	{
		return name.StartsWith("#spec_", StringComparison.OrdinalIgnoreCase) ? null : server.FindChannel(name);
	}

	private User? FindUser(string nick)
	{
		return registry.Online.Values.FirstOrDefault(user => IrcView.IsNick(user.Record, nick))?.User;
	}

	private async Task JoinAsync(IrcSocket session, Session actor, IrcMessage message,
		CancellationToken ct)
	{
		if (message.Parameters.Count == 0)
		{
			NeedMore(session, IrcVerb.Join);
			return;
		}

		foreach (var name in message.Parameters[0].Split(',', StringSplitOptions.RemoveEmptyEntries))
		{
			if (name.StartsWith("#spec_", StringComparison.OrdinalIgnoreCase))
			{
				session.Numeric(IrcNumericReply.ErrInviteOnlyChan, name, replies.Text(IrcReply.CannotJoinSpectator));
				continue;
			}

			var result = await sender.Send(new JoinChannel(actor, IrcTarget(name)), ct);
			switch (result)
			{
				case ChannelJoinResult.Closed or ChannelJoinResult.NotFound:
					session.Numeric(IrcNumericReply.ErrNoSuchChannel, name, replies.Text(IrcReply.NoSuchChannel));
					break;
				case ChannelJoinResult.AccessDenied:
					session.Numeric(IrcNumericReply.ErrInviteOnlyChan, name, replies.Text(IrcReply.CannotJoinNoAccess));
					break;
			}
		}
	}

	private async Task PartAsync(IrcSocket session, Session actor, IrcMessage message,
		CancellationToken ct)
	{
		if (message.Parameters.Count == 0)
		{
			NeedMore(session, IrcVerb.Part);
			return;
		}

		foreach (var name in message.Parameters[0].Split(',', StringSplitOptions.RemoveEmptyEntries))
		{
			var result = await sender.Send(new PartChannel(actor, IrcTarget(name)), ct);
			if (result == ChannelJoinResult.NotFound)
				session.Numeric(IrcNumericReply.ErrNoSuchChannel, name, replies.Text(IrcReply.NoSuchChannel));
			else if (result == ChannelJoinResult.NotMember)
				session.Numeric(IrcNumericReply.ErrNotOnChannel, name, replies.Text(IrcReply.NotOnChannel));
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
			text is null ? replies.Text(IrcReply.NoLongerAway) : replies.Text(IrcReply.NowAway));

		var line = IrcMessage.Away(IrcView.Mask(actor.Record, Options.Domain), text);
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

		var channel = message.Command == IrcVerb.Kick ? message.Parameters[0] : message.Parameters[1];
		if (!channel.StartsWith('#') || Resolve(channel) is null)
			session.Numeric(IrcNumericReply.ErrNoSuchChannel, channel, replies.Text(IrcReply.NoSuchChannel));
		else
			session.Numeric(IrcNumericReply.ErrChanOPrivsNeeded, channel, replies.Text(IrcReply.NoManagement));
	}

	private void Topic(IrcSocket session, Session actor, IrcMessage message)
	{
		if (message.Parameters.Count == 0)
		{
			NeedMore(session, IrcVerb.Topic);
			return;
		}

		var name = message.Parameters[0];
		if (Resolve(name) is not { } channel)
		{
			session.Numeric(IrcNumericReply.ErrNoSuchChannel, name, replies.Text(IrcReply.NoSuchChannel));
			return;
		}

		if (message.Parameters.Count > 1)
			session.Numeric(IrcNumericReply.ErrChanOPrivsNeeded, channel.Name, replies.Text(IrcReply.NoManagement));
		else if (!channel.Members.Contains(actor))
			session.Numeric(IrcNumericReply.ErrNotOnChannel, channel.Name, replies.Text(IrcReply.NotOnChannel));
		else
			IrcChannels.SendTopic(session, channel, replies);
	}

	private void Mode(IrcSocket session, Session actor, IrcMessage message)
	{
		if (message.Parameters.Count == 0)
		{
			NeedMore(session, IrcVerb.Mode);
			return;
		}

		var target = message.Parameters[0];
		if (target.StartsWith('#'))
		{
			if (Resolve(target) is not { } channel)
				session.Numeric(IrcNumericReply.ErrNoSuchChannel, target, replies.Text(IrcReply.NoSuchChannel));
			else if (message.Parameters.Count == 1)
				session.NumericPlain(IrcNumericReply.RplChannelModeIs, channel.Name, "+");
			else if (message.Parameters.Count == 2 && message.Parameters[1].TrimStart('+') == "b")
				session.Numeric(IrcNumericReply.RplEndOfBanList, channel.Name, replies.Text(IrcReply.EndOfBanList));
			else
				session.Numeric(IrcNumericReply.ErrChanOPrivsNeeded, channel.Name, replies.Text(IrcReply.NoManagement));
		}
		else if (!IrcView.IsNick(actor.Record, target))
		{
			session.Numeric(IrcNumericReply.ErrUsersDontMatch, replies.Text(IrcReply.UsersDontMatch));
		}
		else
		{
			// The server has no user modes: a client's own housekeeping, such as "+i", is answered with the empty set.
			session.NumericPlain(IrcNumericReply.RplUModeIs, "+");
		}
	}
}