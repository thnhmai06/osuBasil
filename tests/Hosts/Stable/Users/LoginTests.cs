using Basil.Testing.Clients;

namespace Basil.Host.Stable.Tests.Users;

/// <summary>Logging in as the osu! stable client does: the first reply and the refusals.</summary>
[Collection("Stable host")]
public sealed class LoginTests(StableHostFixture fixture)
{
	private BasilHost Host => fixture.Host;

	[Fact]
	public async Task CorrectPassword_IdTokenVersion()
	{
		using var client = await Host.NewClientAsync("LoginOk");

		var packets = await client.LoginAsync();

		Assert.NotNull(client.Token);
		Assert.True(client.LoginCode > 0);
		Assert.Equal(19, packets.Single(packet => packet.Id == OsuClient.In.ProtocolVersion).AsInt());
		Assert.Equal(OsuClient.In.ProtocolVersion, packets[0].Id);
	}

	[Fact]
	public async Task WrongPassword_MinusOne()
	{
		using var client = await Host.NewClientAsync("LoginWrong");

		await client.LoginAsync("not-the-password");

		Assert.Equal(-1, client.LoginCode);
	}

	[Fact]
	public async Task UnknownUser_MinusOne()
	{
		using var client = OsuClient.Create(Host, "NoSuchUser", "password");

		await client.LoginAsync();

		Assert.Equal(-1, client.LoginCode);
	}

	[Fact]
	public async Task PollUnknownToken_ReconnectAsked()
	{
		using var client = await Host.NewClientAsync("PollUnknown");
		await client.LoginAsync();

		var packets = await client.SendWithTokenAsync("not-a-token");

		Assert.Contains(packets, packet => packet.Id == OsuClient.In.Restart);
	}
}