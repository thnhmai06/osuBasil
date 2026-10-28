using Basil.Application.Chat.Runtime;
using Basil.Application.Common;
using Basil.Application.Users.Connections;
using Basil.Application.Users.Users;
using Basil.Host.Irc.Support;
using Basil.Protocol.Irc;

namespace Basil.Host.Irc;

internal sealed partial class IrcCommands
{
	private async Task MessageAsync(IrcSession session, Connection by, IrcMessage message,
		CancellationToken cancellationToken)
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
		Channel? channel;
		if (target.StartsWith('#'))
		{
			channel = Resolve(target);
			if (channel is null)
			{
				session.Numeric(IrcNumericReply.ErrNoSuchChannel, target, IrcReplies.NoSuchChannel);
				return;
			}
		}
		else
		{
			if (FindUser(target) is not { } user)
			{
				session.Numeric(IrcNumericReply.ErrNoSuchNick, target, IrcReplies.NoSuchNick);
				return;
			}

			channel = pmChannels.Of(user);
		}

		if (!await MayChatAsync(session, by, message.Command, target, cancellationToken)) return;

		var result = await channel.ScopeTo(by).PostAsync(IrcText.Limit(message.Parameters[1]), notice,
			cancellationToken);
		ReplyToPost(session, target, result);
	}

	private async Task<bool> MayChatAsync(IrcSession session, Connection by, string command, string target,
		CancellationToken cancellationToken)
	{
		var limit = (await settings.GetAsync(cancellationToken)).RateLimits.Chat;
		if (limiter.TryAcquire($"chat:{by.Record.Id}", limit.Count, limit.Window)) return true;

		if (session.Has("standard-replies"))
			session.Send(new IrcMessage("FAIL", session.ServerName, command, IrcReplies.RateLimitedCode, target,
				IrcReplies.ChatTooFast));
		else
			session.Send(new IrcMessage("NOTICE", session.ServerName, session.Nick, IrcReplies.ChatTooFast));
		return false;
	}

	private static void ReplyToPost(IrcSession session, string target, Result<ChannelOutcome> result)
	{
		if (result.Missing is not null)
		{
			session.Numeric(IrcNumericReply.ErrCannotSendToChannel, target, IrcReplies.Silenced);
			return;
		}

		switch (result.Outcome)
		{
			case ChannelOutcome.Closed:
				session.Numeric(IrcNumericReply.ErrNoSuchChannel, target, IrcReplies.NoSuchChannel);
				break;
			case ChannelOutcome.NotMember or ChannelOutcome.AccessDenied:
				session.Numeric(IrcNumericReply.ErrCannotSendToChannel, target, IrcReplies.CannotSend);
				break;
			case ChannelOutcome.Blocked:
				session.Numeric(IrcNumericReply.ErrCannotSendToChannel, target, IrcReplies.DmBlocked);
				break;
			case ChannelOutcome.TargetSilenced:
				session.Numeric(IrcNumericReply.ErrCannotSendToChannel, target, IrcReplies.TargetSilenced);
				break;
			case ChannelOutcome.Empty:
				session.Numeric(IrcNumericReply.ErrNoTextToSend, "No text to send");
				break;
		}
	}

	/// <summary>Finds the open channel an IRC client means by a name.</summary>
	/// <param name="name">The channel name.</param>
	/// <returns>The channel, or <see langword="null" /> when none matches or the name is one IRC never reaches.</returns>
	private Channel? Resolve(string name)
	{
		return name.StartsWith("#spec_", StringComparison.OrdinalIgnoreCase) ? null : server.FindChannel(name);
	}

	private User? FindUser(string nick)
	{
		return registry.Online.Values.FirstOrDefault(user => IrcView.IsNick(user.Record, nick));
	}

	private void Join(IrcSession session, Connection by, IrcMessage message)
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

			if (Resolve(name) is not { } channel)
			{
				session.Numeric(IrcNumericReply.ErrNoSuchChannel, name, IrcReplies.NoSuchChannel);
				continue;
			}

			switch (channel.ScopeTo(by).Join().Outcome)
			{
				case ChannelOutcome.Closed:
					session.Numeric(IrcNumericReply.ErrNoSuchChannel, name, IrcReplies.NoSuchChannel);
					break;
				case ChannelOutcome.AccessDenied:
					session.Numeric(IrcNumericReply.ErrInviteOnlyChan, name, IrcReplies.CannotJoin);
					break;
			}
		}
	}

	private void Part(IrcSession session, Connection by, IrcMessage message)
	{
		if (message.Parameters.Count == 0)
		{
			NeedMore(session, "PART");
			return;
		}

		foreach (var name in message.Parameters[0].Split(',', StringSplitOptions.RemoveEmptyEntries))
		{
			if (Resolve(name) is not { } channel)
				session.Numeric(IrcNumericReply.ErrNoSuchChannel, name, IrcReplies.NoSuchChannel);
			else if (channel.ScopeTo(by).Part().Outcome == ChannelOutcome.NotMember)
				session.Numeric(IrcNumericReply.ErrNotOnChannel, name, IrcReplies.NotOnChannel);
		}
	}

	private void Away(IrcSession session, Connection by, IrcMessage message)
	{
		var text = message.Parameters.Count > 0 && message.Parameters[0].Length > 0 ? message.Parameters[0] : null;
		by.User.ScopeTo(by).Profile.SetAway(text);
		session.Numeric(text is null ? IrcNumericReply.RplUnaway : IrcNumericReply.RplNowAway,
			text is null ? "You are no longer marked as being away" : "You have been marked as being away");

		var line = new IrcMessage("AWAY", IrcView.Mask(by.Record, Options.Domain).ToString(), text is null ? [] : [text]) { ForceTrailing = true };
		var mine = presence.Of(by.Record);
		foreach (var other in clients.All.Where(other => other.Has("away-notify") && other.Connection is { } connection &&
		                                                  !ReferenceEquals(connection.User, by.User) &&
		                                                  mine.Any(channel => channel.Members.Contains(connection))))
			other.Deliver(line);
	}

	private void Management(IrcSession session, IrcMessage message)
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

	private void Topic(IrcSession session, Connection by, IrcMessage message)
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
		else if (!channel.Members.Contains(by))
			session.Numeric(IrcNumericReply.ErrNotOnChannel, channel.Name, IrcReplies.NotOnChannel);
		else
			IrcChannels.SendTopic(session, channel);
	}

	private void Mode(IrcSession session, Connection by, IrcMessage message)
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
		else if (!IrcView.IsNick(by.Record, target))
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
