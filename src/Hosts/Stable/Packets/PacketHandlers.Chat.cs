using Basil.Application.Chat.Messages;
using Basil.Application.Chat.Objects;
using Basil.Application.Users.Objects.Connections;
using Basil.Host.Stable.Support;
using Basil.Protocol.Stable.Models.Chat;

namespace Basil.Host.Stable.Packets;

internal sealed partial class PacketHandlers
{
	private async Task JoinChannelAsync(StableConnection osu, string name, CancellationToken cancellationToken)
	{
		var result = await sender.Send(new JoinChannel(osu, TargetOf(name)), cancellationToken);
		if (!result.IsOk && result.Outcome != ChannelOutcome.AlreadyMember)
			outbox.Send(osu, writer => writer.ChannelKick(name));
	}

	private async Task PartChannelAsync(StableConnection osu, string name, CancellationToken cancellationToken)
	{
		// The client leaves the match and spectator channels together with the match or the host.
		if (name is StableView.MultiplayerAlias or StableView.SpectatorAlias) return;
		await sender.Send(new PartChannel(osu, TargetOf(name)), cancellationToken);
	}

	private async Task<bool> MayChatAsync(StableConnection osu, CancellationToken cancellationToken)
	{
		var limit = (await settings.GetAsync(cancellationToken)).RateLimits.Chat;
		if (limiter.TryAcquire($"chat:{osu.Record.Id}", limit.Count, limit.Window)) return true;
		outbox.Send(osu, writer => writer.Notification(StableReplies.ChatTooFast));
		return false;
	}

	private static string Limit(string text)
	{
		return text.Length > StableReplies.MaxMessageLength ? text[..StableReplies.MaxMessageLength] : text;
	}

	private static ChatTarget TargetOf(string name)
	{
		return name switch
		{
			StableView.MultiplayerAlias => new ChatTarget.OwnRoom(),
			StableView.SpectatorAlias => new ChatTarget.OwnSpectators(),
			_ when RoomChannel.TryParseName(name, out var roomId) => new ChatTarget.Room(roomId),
			_ => new ChatTarget.General(name)
		};
	}

	private async Task PublicMessageAsync(StableConnection osu, StableMessage message,
		CancellationToken cancellationToken)
	{
		if (!await MayChatAsync(osu, cancellationToken)) return;
		await sender.Send(new PostChatMessage(osu, TargetOf(message.Recipient), Limit(message.Text)),
			cancellationToken);
	}

	private async Task PrivateMessageAsync(StableConnection osu, StableMessage message,
		CancellationToken cancellationToken)
	{
		if (await users.GetByNameAsync(message.Recipient, cancellationToken) is not { } record) return;
		if (!registry.Of(record).IsOnline) return;
		if (!await MayChatAsync(osu, cancellationToken)) return;
		var result = await sender.Send(new PostChatMessage(osu, new ChatTarget.Private(record), Limit(message.Text)),
			cancellationToken);
		switch (result.Outcome)
		{
			case ChannelOutcome.Blocked when result.Missing is null:
				outbox.Send(osu, writer => writer.UserDmBlocked(message.Recipient));
				break;
			case ChannelOutcome.TargetSilenced when result.Missing is null:
				outbox.Send(osu, writer => writer.TargetSilenced(message.Recipient));
				break;
		}
	}
}