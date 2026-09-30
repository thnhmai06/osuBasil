using Basil.Domain.Users;
using Basil.Application.Common.Notifications;
using Basil.Application.Common.Persistence;
using Basil.Application.Sessions;

namespace Basil.Application.Chat;

/// <summary>Routes an outgoing chat message to a channel or a private recipient.</summary>
/// <remarks>
///     A leading <c>#</c> routes to the channel path; anything else is a username. A command
///     (recognized by <see cref="ChatCommands" />) still delivers the original message to the
///     channel's members, then also delivers its reply.
/// </remarks>
public sealed class Messaging(
	IChannelRegistry channels,
	ISessionRegistry<GameSession> games,
	IRepository<int, User> usersById,
	ChatCommands commands)
{
	/// <summary>Sends a chat message from <paramref name="from" /> to a channel or a user.</summary>
	/// <param name="from">The session sending the message.</param>
	/// <param name="target">A <c>#</c>-prefixed channel name, or the username of the recipient.</param>
	/// <param name="text">The message body.</param>
	/// <param name="cancellationToken">A token that cancels the send.</param>
	public async Task SendAsync(UserSession from, string target, string text,
		CancellationToken cancellationToken = default)
	{
		if (from.User.Value.SilenceEnd is { } silenceEnd && silenceEnd > DateTimeOffset.UtcNow)
		{
			from.Notify(new NotificationRefused(target, RefusalReason.Silenced));
			return;
		}

		if (target.StartsWith('#'))
			await SendToChannelAsync(from, target, text, cancellationToken);
		else
			await SendToUserAsync(from, target, text, cancellationToken);
	}

	private async Task SendToChannelAsync(UserSession from, string channelName, string text,
		CancellationToken cancellationToken)
	{
		if (!channels.AllByName.TryGetValue(channelName, out var membership) ||
		    !membership.Members.Contains(from) ||
		    !membership.CanWrite(from.User))
		{
			from.Notify(new NotificationRefused(channelName, RefusalReason.NoWritePermission));
			return;
		}

		NotifyMembers(membership, from, new ChatNotification(from.User, channelName, text));

		if (await commands.ExecuteAsync(from, channelName, text, cancellationToken) is { } reply &&
		    await usersById.GetAsync(SystemUserIds.BasilBot, cancellationToken) is { } basilBot)
			NotifyMembers(membership, null, new ChatNotification(basilBot, channelName, reply));
	}

	private async Task SendToUserAsync(UserSession from, string username, string text,
		CancellationToken cancellationToken)
	{
		if (games.AllByUser.Values.FirstOrDefault(s => s.User.Value.Name.Equals(username, StringComparison.OrdinalIgnoreCase))
		    is not GameSession target)
			// Not currently online: nothing to check against, nothing to deliver.
			return;

		if (target.User.Value.SilenceEnd is { } silenceEnd && silenceEnd > DateTimeOffset.UtcNow)
		{
			from.Notify(new NotificationRefused(username, RefusalReason.Silenced));
			return;
		}

		target.Notify(new ChatNotification(from.User, username, text));

		if (target.AwayMessage is { } awayMessage)
			from.Notify(new ChatNotification(target.User, from.User.Value.Name, awayMessage));

		if (await commands.ExecuteAsync(from, null, text, cancellationToken) is { } reply &&
		    await usersById.GetAsync(SystemUserIds.BasilBot, cancellationToken) is { } basilBot)
			from.Notify(new ChatNotification(basilBot, username, reply));
	}

	private static void NotifyMembers(ChannelSession channel, UserSession? skip, ChatNotification notification)
	{
		foreach (var member in channel.Members)
		{
			if (ReferenceEquals(member, skip)) continue;
			member.Notify(notification);
		}
	}
}