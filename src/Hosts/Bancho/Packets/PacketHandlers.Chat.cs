using Basil.Application.Chat.Runtime;
using Basil.Application.Users.Connections;
using Basil.Host.Bancho.Support;
using Basil.Protocol.Bancho.Models.Chat;

namespace Basil.Host.Bancho.Packets;

internal sealed partial class PacketHandlers
{
	private void JoinChannel(BanchoConnection osu, string name)
	{
		var result = channels.Resolve(name, osu) is { } channel ? channel.ScopeTo(osu).Join() : default;
		if (!result.IsOk && result.Outcome != ChannelOutcome.AlreadyMember)
			outbox.Send(osu, writer => writer.ChannelKick(name));
	}

	private void PartChannel(BanchoConnection osu, string name)
	{
		// The client leaves the match and spectator channels together with the match or the host.
		if (name is BanchoView.MultiplayerAlias or BanchoView.SpectatorAlias) return;
		channels.Resolve(name, osu)?.ScopeTo(osu).Part();
	}

	private async Task<bool> MayChatAsync(BanchoConnection osu, CancellationToken cancellationToken)
	{
		var limit = (await settings.GetAsync(cancellationToken)).RateLimits.Chat;
		if (limiter.TryAcquire($"chat:{osu.Record.Id}", limit.Count, limit.Window)) return true;
		outbox.Send(osu, writer => writer.Notification(BanchoReplies.ChatTooFast));
		return false;
	}

	private static string Limit(string text)
	{
		return text.Length > BanchoReplies.MaxMessageLength ? text[..BanchoReplies.MaxMessageLength] : text;
	}

	private async Task PublicMessageAsync(BanchoConnection osu, BanchoMessage message, CancellationToken cancellationToken)
	{
		if (channels.Resolve(message.Recipient, osu) is not { } channel) return;
		if (!await MayChatAsync(osu, cancellationToken)) return;
		await channel.ScopeTo(osu).PostAsync(Limit(message.Text), cancellationToken: cancellationToken);
	}

	private async Task PrivateMessageAsync(BanchoConnection osu, BanchoMessage message, CancellationToken cancellationToken)
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
