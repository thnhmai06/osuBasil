using Basil.Testing.Clients;

namespace Basil.Host.Bancho.Tests.Users;

/// <summary>Who sees whom online, and friends.</summary>
[Collection("Bancho host")]
public sealed class PresenceTests(BanchoHostFixture fixture)
{
	private BasilHost Host => fixture.Host;

	[Fact]
	public async Task Login_ShowsBasilBotAsAnOnlineUser()
	{
		using var client = await Host.NewClientAsync("SeesBot");

		var packets = await client.LoginAsync();

		var bot = packets.Where(packet => packet.Id == OsuClient.In.UserPresence).Select(packet => packet.AsInt());
		Assert.Contains(1, bot);
	}

	[Fact]
	public async Task Login_OfASecondUser_ReachesTheFirstOnItsNextPoll()
	{
		using var first = await Host.NewClientAsync("FirstOnline");
		using var second = await Host.NewClientAsync("SecondOnline");
		await first.LoginAsync();

		await second.LoginAsync();
		await first.WaitForAsync(OsuClient.In.UserPresence, packet => packet.AsInt() == second.LoginCode);

		Assert.Contains(first.Received, packet => packet.Id == OsuClient.In.UserPresence &&
		                                          packet.AsInt() == second.LoginCode);
		Assert.Contains(second.Received, packet => packet.Id == OsuClient.In.UserPresence &&
		                                           packet.AsInt() == first.LoginCode);
	}

	[Fact]
	public async Task Logout_IsAnnouncedToOthers()
	{
		using var stays = await Host.NewClientAsync("StaysOnline");
		using var leaves = await Host.NewClientAsync("LeavesOnline");
		await stays.LoginAsync();
		await leaves.LoginAsync();
		// The server ignores a logout that arrives within a second of login.
		Host.Time.Advance(TimeSpan.FromSeconds(2));

		await leaves.LogoutAsync();
		await stays.WaitForAsync(OsuClient.In.UserLogout, packet => packet.AsInt() == leaves.LoginCode);

		Assert.Contains(stays.Received, packet => packet.Id == OsuClient.In.UserLogout &&
		                                          packet.AsInt() == leaves.LoginCode);
	}

	[Fact]
	public async Task FriendAdd_AppearsInTheFriendList()
	{
		using var owner = await Host.NewClientAsync("FriendOwner");
		using var friend = await Host.NewClientAsync("FriendTarget");
		await owner.LoginAsync();
		await friend.LoginAsync();

		await owner.SendAsync(writer => writer.Int(OsuClient.Out.FriendAdd, friend.LoginCode));
		var (status, text) = await owner.GetFriendsAsync();

		Assert.Equal(System.Net.HttpStatusCode.OK, status);
		Assert.Equal(friend.LoginCode.ToString(), text);
	}

	[Fact]
	public async Task WebEndpoint_WrongPassword_IsUnauthorized()
	{
		using var client = await Host.NewClientAsync("WebWrongPw");
		await client.LoginAsync();
		using var wrong = OsuClient.Create(Host, "WebWrongPw", "not-it");

		var (status, _) = await wrong.GetFriendsAsync();

		Assert.Equal(System.Net.HttpStatusCode.Unauthorized, status);
	}
}
