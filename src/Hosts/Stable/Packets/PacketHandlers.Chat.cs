using Basil.Application.Common.Settings;
using Basil.Application.Chat.Messages;
using Basil.Application.Chat.Objects;
using Basil.Application.Users.Messages;
using Basil.Application.Users.Objects;
using Basil.Application.Users.Objects.Connections;
using Basil.Domain.Users;
using Basil.Host.Stable.Support;
using Basil.Protocol.Stable.Models.Chat;

namespace Basil.Host.Stable.Packets;

internal sealed partial class PacketHandlers
{
	private async Task JoinChannelAsync(StableConnection osu, string name, CancellationToken ct)
	{
		var result = await sender.Send(new JoinChannel(osu, TargetOf(name)), ct);
		if (result != ChannelOutcome.Ok && result != ChannelOutcome.AlreadyMember)
			outbox.Send(osu, writer => writer.ChannelKick(name));
	}

	private async Task PartChannelAsync(StableConnection osu, string name, CancellationToken ct)
	{
		// The client leaves the match and spectator channels together with the match or the host.
		if (name is StableView.MultiplayerAlias or StableView.SpectatorAlias) return;
		await sender.Send(new PartChannel(osu, TargetOf(name)), ct);
	}

	private async Task<bool> MayChatAsync(StableConnection osu, CancellationToken ct)
	{
		var limit = (await sender.Send(new ReadSettings(), ct)).RateLimits.Chat;
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
		CancellationToken ct)
	{
		if (!await MayChatAsync(osu, ct)) return;
		await sender.Send(new PostChatMessage(osu, TargetOf(message.Recipient), Limit(message.Text)),
			ct);
	}

	private async Task PrivateMessageAsync(StableConnection osu, StableMessage message,
		CancellationToken ct)
	{
		var name = UserData.NormalizeName(message.Recipient);
		if (registry.Online.Keys.FirstOrDefault(user => UserData.NormalizeName(user.Value.Name) == name) is not { } record)
			return;
		if (!await MayChatAsync(osu, ct)) return;
		var result = await sender.Send(new PostChatMessage(osu, new ChatTarget.Private(record.Id), Limit(message.Text)),
			ct);
		switch (result)
		{
			case ChannelOutcome.Blocked:
				outbox.Send(osu, writer => writer.UserDmBlocked(message.Recipient));
				break;
			case ChannelOutcome.TargetSilenced:
				outbox.Send(osu, writer => writer.TargetSilenced(message.Recipient));
				break;
		}
	}
}