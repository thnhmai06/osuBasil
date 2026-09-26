using Basil.Application.Contracts.Registries;
using Basil.Application.Contracts.Repositories;
using Basil.Application.Models.Notifications;
using Basil.Application.Models.Sessions;
using Basil.Domain.Chat;
using Basil.Domain.Users;

namespace Basil.Application.Services.Operations;

/// <summary>Routes an outgoing chat message to a channel or a private recipient.</summary>
/// <remarks>
///     A leading <c>#</c> routes to the channel path; anything else is a username. A command
///     (recognized by <see cref="ChatCommands" />) still delivers the original message to the
///     channel's members, then also delivers its reply.
/// </remarks>
public sealed class Messaging(
	IChannelRegistry channels,
	IRepository<string, ChatChannel> channelRepository,
	IPlayerRegistry players,
	IRepository<int, User> users,
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
		var sender = await users.LoadAsync(from.UserId, cancellationToken);
		if (sender is null) return;

		if (sender.SilenceEnd is { } silenceEnd && silenceEnd > DateTimeOffset.UtcNow)
		{
			from.Notify(new MessageRefused(target, RefusalReason.Silenced));
			return;
		}

		if (target.StartsWith('#'))
			await SendToChannelAsync(from, sender, target, text, cancellationToken);
		else
			await SendToUserAsync(from, target, text, cancellationToken);
	}

	private async Task SendToChannelAsync(UserSession from, User sender, string channelName, string text,
		CancellationToken cancellationToken)
	{
		var channel = await channelRepository.LoadAsync(channelName, cancellationToken);
		if (channel is null ||
		    !channels.AllByName.TryGetValue(channelName, out var membership) ||
		    !membership.MemberIds.Contains(from.UserId) ||
		    !ChannelSession.CanWrite(channel, sender))
		{
			from.Notify(new MessageRefused(channelName, RefusalReason.NoWritePermission));
			return;
		}

		NotifyMembers(membership, from.UserId, new ChatMessage(from.UserId, channelName, text));

		if (await commands.ExecuteAsync(from, channelName, text, cancellationToken) is { } reply)
			NotifyMembers(membership, null, new ChatMessage(SystemUserIds.BasilBot, channelName, reply));
	}

	private async Task SendToUserAsync(UserSession from, string username, string text,
		CancellationToken cancellationToken)
	{
		if (players.FindByName(username) is not GameSession target)
			// Not currently online: nothing to check against, nothing to deliver.
			return;

		var targetUser = await users.LoadAsync(target.UserId, cancellationToken);
		if (targetUser?.SilenceEnd is { } silenceEnd && silenceEnd > DateTimeOffset.UtcNow)
		{
			from.Notify(new MessageRefused(username, RefusalReason.Silenced));
			return;
		}

		target.Notify(new ChatMessage(from.UserId, username, text));

		if (target.AwayMessage is { } awayMessage)
			from.Notify(new ChatMessage(target.UserId, from.UserId.ToString(), awayMessage));

		if (await commands.ExecuteAsync(from, null, text, cancellationToken) is { } reply)
			from.Notify(new ChatMessage(SystemUserIds.BasilBot, username, reply));
	}

	private void NotifyMembers(ChannelSession channel, int? skipUserId, ChatMessage message)
	{
		foreach (var memberId in channel.MemberIds)
		{
			if (memberId == skipUserId) continue;
			if (players.AllById.TryGetValue(memberId, out var member))
				member.Notify(message);
		}
	}
}