using Basil.Host.Bancho.Support;
using Basil.Testing.Clients;

namespace Basil.Host.Bancho.Tests.Chat;

/// <summary>Chat as an osu! client sees it: channel messages, private messages and the limits.</summary>
[Collection("Bancho host")]
public sealed class ChatTests(BanchoHostFixture fixture)
{
	private BasilHost Host => fixture.Host;

	private static IEnumerable<ServerPacket> Messages(OsuClient client) =>
		client.Received.Where(packet => packet.Id == OsuClient.In.SendMessage);

	[Fact]
	public async Task ChannelMessage_ReachesTheOtherMember_AndNotTheSender()
	{
		using var sender = await Host.NewClientAsync("ChatSender");
		using var listener = await Host.NewClientAsync("ChatListener");
		await sender.LoginAsync();
		await listener.LoginAsync();
		await sender.PollAsync();
		await listener.PollAsync();
		sender.ClearReceived();
		listener.ClearReceived();

		await sender.SendAsync(writer => writer.Packet(OsuClient.Out.SendPublicMessage,
			ClientPacketWriter.Message("hello channel", "#osu")));
		await listener.WaitForAsync(OsuClient.In.SendMessage);
		await sender.PollAsync();

		var heard = Assert.Single(Messages(listener));
		Assert.Equal(["ChatSender", "hello channel", "#osu"], heard.Strings(3));
		Assert.Empty(Messages(sender));
	}

	[Fact]
	public async Task PrivateMessage_ReachesTheRecipientNamedByUsername()
	{
		using var sender = await Host.NewClientAsync("PmSender");
		using var recipient = await Host.NewClientAsync("PmRecipient");
		await sender.LoginAsync();
		await recipient.LoginAsync();
		recipient.ClearReceived();

		await sender.SendAsync(writer => writer.Packet(OsuClient.Out.SendPrivateMessage,
			ClientPacketWriter.Message("psst", "PmRecipient")));
		await recipient.WaitForAsync(OsuClient.In.SendMessage);

		var heard = Assert.Single(Messages(recipient));
		Assert.Equal(["PmSender", "psst", "PmRecipient"], heard.Strings(3));
	}

	[Fact]
	public async Task PrivateMessage_ToSomeoneWhoBlocksNonFriends_TellsTheSender()
	{
		using var sender = await Host.NewClientAsync("DmBlockedSender");
		using var recipient = await Host.NewClientAsync("DmBlocker");
		await sender.LoginAsync();
		await recipient.LoginAsync();
		await recipient.SendAsync(writer => writer.Int(OsuClient.Out.ToggleBlockNonFriendDms, 1));

		await sender.SendAsync(writer => writer.Packet(OsuClient.Out.SendPrivateMessage,
			ClientPacketWriter.Message("hi", "DmBlocker")));

		Assert.Contains(sender.Received, packet => packet.Id == OsuClient.In.UserDmBlocked);
	}

	[Fact]
	public async Task LongMessage_IsCutAtTheLimit()
	{
		using var sender = await Host.NewClientAsync("LongSender");
		using var listener = await Host.NewClientAsync("LongListener");
		await sender.LoginAsync();
		await listener.LoginAsync();
		listener.ClearReceived();

		await sender.SendAsync(writer => writer.Packet(OsuClient.Out.SendPublicMessage,
			ClientPacketWriter.Message(new string('x', BanchoReplies.MaxMessageLength + 50), "#osu")));
		await listener.WaitForAsync(OsuClient.In.SendMessage);

		var heard = Assert.Single(Messages(listener));
		Assert.Equal(BanchoReplies.MaxMessageLength, heard.Strings(3)[1].Length);
	}

	[Fact]
	public async Task Flooding_IsRefusedWithANotice()
	{
		using var sender = await Host.NewClientAsync("Flooder");
		await sender.LoginAsync();
		sender.ClearReceived();

		// The default allows 10 messages per window; the clock does not move, so the 11th is too many.
		await sender.SendAsync(writer =>
		{
			for (var index = 0; index < 11; index++)
				writer.Packet(OsuClient.Out.SendPublicMessage, ClientPacketWriter.Message($"m{index}", "#osu"));
		});

		var notice = sender.Received.Last(packet => packet.Id == OsuClient.In.Notification);
		Assert.Equal(BanchoReplies.ChatTooFast, notice.Strings(1)[0]);
	}
}
