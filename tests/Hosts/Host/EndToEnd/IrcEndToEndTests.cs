using Basil.Testing.Clients;

namespace Basil.Host.Tests.EndToEnd;

/// <summary>The IRC host joins the same account and the same chat as the osu! client.</summary>
[Collection("Host end to end")]
public sealed class IrcEndToEndTests(HostEndToEndFixture fixture)
{
	private BasilHost Host => fixture.Host;

	[Fact]
	public async Task Pass_Registration_SignsIn_AndChatReachesTheOsuClientInBothDirections()
	{
		var (irc, key) = await Host.NewIrcClientAsync("E2eIrcChat");
		using var ircClient = irc;
		await irc.RegisterAsync("E2eIrcChat", key);
		await irc.ReadUntilAsync(line => IrcClient.CommandOf(line) == "366" && line.Contains(" #osu "));
		using var osu = await Host.NewClientAsync("E2eIrcOsu");
		await osu.LoginAsync();
		await osu.PollAsync();
		osu.ClearReceived();
		irc.Clear();

		await irc.SendAsync("PRIVMSG #osu :hello from irc");
		await osu.WaitForAsync(OsuClient.In.SendMessage);
		await osu.SendAsync(writer => writer.Packet(OsuClient.Out.SendPublicMessage,
			ClientPacketWriter.Message("hello from osu", "#osu")));

		var heard = Assert.Single(osu.Received, packet => packet.Id == OsuClient.In.SendMessage);
		Assert.Equal(["E2eIrcChat", "hello from irc", "#osu"], heard.Strings(3));
		Assert.Equal(":E2eIrcOsu!E2eIrcOsu@basil.test PRIVMSG #osu :hello from osu",
			await irc.ExpectAsync("PRIVMSG"));
	}

	[Fact]
	public async Task SaslRegistration_WithTheSameKey_SignsInAsTheAccount()
	{
		var (irc, key) = await Host.NewIrcClientAsync("E2eIrcSasl");
		using var ircClient = irc;

		var outcome = await irc.RegisterWithSaslAsync("E2eIrcSasl", "E2eIrcSasl", key);
		var welcome = await irc.ExpectAsync("001");

		Assert.Equal(":irc.basil.test 903 E2eIrcSasl :SASL authentication successful", outcome);
		Assert.StartsWith(":irc.basil.test 001 E2eIrcSasl ", welcome);
	}
}