using Basil.Testing.Clients;

namespace Basil.Host.Bancho.Tests.Users;

/// <summary>Logging in as the osu! stable client does: the first reply and the refusals.</summary>
[Collection("Bancho host")]
public sealed class LoginTests(BanchoHostFixture fixture)
{
	private BasilHost Host => fixture.Host;

	[Fact]
	public async Task Login_CorrectPassword_ReturnsUserIdTokenAndProtocolVersion()
	{
		using var client = await Host.NewClientAsync("LoginOk");

		var packets = await client.LoginAsync();

		Assert.NotNull(client.Token);
		Assert.True(client.LoginCode > 0);
		Assert.Equal(19, packets.Single(packet => packet.Id == OsuClient.In.ProtocolVersion).AsInt());
		Assert.Equal(OsuClient.In.ProtocolVersion, packets[0].Id);
	}

	[Fact]
	public async Task Login_WrongPassword_ReplyIsMinusOne()
	{
		using var client = await Host.NewClientAsync("LoginWrong");

		await client.LoginAsync("not-the-password");

		Assert.Equal(-1, client.LoginCode);
	}

	[Fact]
	public async Task Login_UnknownUser_ReplyIsMinusOne()
	{
		using var client = OsuClient.Create(Host, "NoSuchUser", "password");

		await client.LoginAsync();

		Assert.Equal(-1, client.LoginCode);
	}

	[Fact]
	public async Task Poll_UnknownToken_AsksTheClientToReconnect()
	{
		using var client = await Host.NewClientAsync("PollUnknown");
		await client.LoginAsync();

		var packets = await client.SendWithTokenAsync("not-a-token");

		Assert.Contains(packets, packet => packet.Id == OsuClient.In.Restart);
	}
}