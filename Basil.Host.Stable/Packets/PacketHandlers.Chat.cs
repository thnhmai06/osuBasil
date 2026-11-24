using Basil.Gateway.RateLimiting;
using Basil.Protocol.Game.Stable.Wire.Chat;
using Basil.Application.Chat.Messages;
using Basil.Application.Shared.Settings;
using Basil.Application.Users;
using Basil.Application.Sessions.Objects;
using Basil.Domain.Chat;
using Basil.Domain.Users;
using Basil.Host.Stable.Support;
using Basil.Protocol.Game.Stable.Models.Chat;
using Basil.Application.Chat.Objects;

namespace Basil.Host.Stable.Packets;

internal sealed partial class PacketHandlers
{
	private async Task JoinChannelAsync(StableSession osu, string name, CancellationToken ct)
	{
		var result = await sender.Send(new JoinChannel(osu, TargetOf(name)), ct);
		if (result != ChannelJoinResult.Ok && result != ChannelJoinResult.AlreadyMember)
			outbox.Send(osu, writer => writer.ChannelKick(name));
	}

	private async Task PartChannelAsync(StableSession osu, string name, CancellationToken ct)
	{
		// The client leaves the match and spectator channels together with the match or the host.
		if (name is StableView.MultiplayerAlias or StableView.SpectatorAlias) return;
		await sender.Send(new PartChannel(osu, TargetOf(name)), ct);
	}

	private async Task<bool> MayChatAsync(StableSession osu, CancellationToken ct)
	{
		var limit = (await sender.Send(new ReadSettings(), ct)).RateLimits.Chat;
		if (limiter.TryAcquire(RateRule.Chat, osu.Record.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), limit.Count, limit.Window)) return true;
		outbox.Send(osu, writer => writer.Notification(StableReplies.ChatTooFast));
		return false;
	}

	private static (string Text, bool Action) ChatText(string wire)
	{
		var action = ChatAction.TryRead(wire, out var body);
		return (Limit(body), action);
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
			_ when RoomChannelRules.TryParse(name, out var roomId) => new ChatTarget.Room(roomId),
			_ => new ChatTarget.General(name)
		};
	}

	private async Task PublicMessageAsync(StableSession osu, StableMessage message,
		CancellationToken ct)
	{
		if (!await MayChatAsync(osu, ct)) return;
		var (text, action) = ChatText(message.Text);
		await sender.Send(new PostChatMessage(osu, TargetOf(message.Recipient), text, Action: action), ct);
	}

	private async Task PrivateMessageAsync(StableSession osu, StableMessage message,
		CancellationToken ct)
	{
		var name = UserData.TryNormalize(message.Recipient);
		if (name is null ||
		    registry.Online.Keys.FirstOrDefault(user => user.Value.NormalizedName == name) is not { } record)
			return;
		if (!await MayChatAsync(osu, ct)) return;
		var (text, action) = ChatText(message.Text);
		var result = await sender.Send(new PostChatMessage(osu, new ChatTarget.Private(record.Id), text, Action: action),
			ct);
		switch (result)
		{
			case ChannelJoinResult.Blocked:
				outbox.Send(osu, writer => writer.UserDmBlocked(message.Recipient));
				break;
			case ChannelJoinResult.TargetSilenced:
				outbox.Send(osu, writer => writer.TargetSilenced(message.Recipient));
				break;
		}
	}
}