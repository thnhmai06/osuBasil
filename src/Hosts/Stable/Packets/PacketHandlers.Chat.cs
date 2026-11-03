using Basil.Application.Chat.Objects;
using Basil.Application.Users.Objects.Connections;
using Basil.Host.Stable.Support;
using Basil.Protocol.Stable.Models.Chat;

namespace Basil.Host.Stable.Packets;

internal sealed partial class PacketHandlers
{
	private void JoinChannel(StableConnection osu, string name)
	{
		var result = channels.Resolve(name, osu) is { } channel ? channel.ScopeTo(osu).Join() : default;
		if (!result.IsOk && result.Outcome != ChannelOutcome.AlreadyMember)
			outbox.Send(osu, writer => writer.ChannelKick(name));
	}

	private void PartChannel(StableConnection osu, string name)
	{
		// The client leaves the match and spectator channels together with the match or the host.
		if (name is StableView.MultiplayerAlias or StableView.SpectatorAlias) return;
		channels.Resolve(name, osu)?.ScopeTo(osu).Part();
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

	private async Task PublicMessageAsync(StableConnection osu, StableMessage message,
		CancellationToken cancellationToken)
	{
		if (channels.Resolve(message.Recipient, osu) is not { } channel) return;
		if (!await MayChatAsync(osu, cancellationToken)) return;
		await channel.ScopeTo(osu).PostAsync(Limit(message.Text), cancellationToken: cancellationToken);
	}

	private async Task PrivateMessageAsync(StableConnection osu, StableMessage message,
		CancellationToken cancellationToken)
	{
		if (await users.GetByNameAsync(message.Recipient, cancellationToken) is not { } record) return;
		var target = registry.Of(record);
		if (!target.IsOnline) return;
		if (!await MayChatAsync(osu, cancellationToken)) return;
		var result = await pmChannels.Of(target).ScopeTo(osu).PostAsync(Limit(message.Text),
			cancellationToken: cancellationToken);
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