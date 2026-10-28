using Basil.Host.Irc.Tests.Support;
using Basil.Testing.Clients;

namespace Basil.Host.Irc.Tests.Chat;

/// <summary>What IRC clients see when users come and go: one JOIN, PART or QUIT per user, however many connections.</summary>
[Collection("IRC host")]
public sealed class PresenceTests(IrcHostFixture fixture)
{
	private BasilHost Host => fixture.Host;

	[Fact]
	public async Task Join_OfAnotherIrcClient_IsShownToTheOthersInTheChannel()
	{
		Host.AddChannel("#presence1");
		using var watcher = await Host.SignInAsync("PrWatcher");
		await watcher.SendAsync("JOIN #presence1");
		await watcher.ExpectFromAsync("366", "#presence1");
		using var joiner = await Host.SignInAsync("PrJoiner");

		await joiner.SendAsync("JOIN #presence1");

		Assert.Equal(":PrJoiner!PrJoiner@basil.test JOIN #presence1", await watcher.ExpectFromAsync("JOIN", "#presence1"));
	}

	[Fact]
	public async Task Part_OfAnotherIrcClient_IsShownToTheOthersInTheChannel()
	{
		using var watcher = await Host.SignInAsync("PrPartWatch");
		using var leaver = await Host.SignInAsync("PrLeaver");
		await watcher.BarrierAsync();

		await leaver.SendAsync("PART #osu");

		Assert.Equal(":PrLeaver!PrLeaver@basil.test PART #osu", await watcher.ExpectFromAsync("PART", "#osu"));
	}

	[Fact]
	public async Task OsuLogin_ShowsAJoinToIrcClientsInTheAutoJoinChannel()
	{
		using var watcher = await Host.SignInAsync("PrOsuWatcher");

		using var osu = await Host.LoginOsuAsync("PrOsuComes");

		Assert.Equal(":PrOsuComes!PrOsuComes@basil.test JOIN #osu", await watcher.ExpectFromAsync("JOIN", "PrOsuComes"));
	}

	[Fact]
	public async Task OsuLogout_ShowsOneQuitAndNoPart()
	{
		using var watcher = await Host.SignInAsync("PrQuitWatcher");
		using var osu = await Host.LoginOsuAsync("PrOsuLeaves");
		await watcher.ExpectFromAsync("JOIN", "PrOsuLeaves");
		Host.Time.Advance(TimeSpan.FromSeconds(2)); // a logout right after login is ignored
		watcher.Clear();

		await osu.LogoutAsync();

		Assert.Equal(":PrOsuLeaves!PrOsuLeaves@basil.test QUIT Quit",
			await watcher.ExpectFromAsync("QUIT", "PrOsuLeaves"));
		var rest = await watcher.BarrierAsync();
		Assert.DoesNotContain(rest.Concat(watcher.Received), line => IrcClient.CommandOf(line) == "PART");
	}

	[Fact]
	public async Task IrcQuit_ShowsOneQuitToTheOthers()
	{
		using var watcher = await Host.SignInAsync("PrQuitSeer");
		using var leaver = await Host.SignInAsync("PrQuitter");
		await watcher.BarrierAsync();

		await leaver.SendAsync("QUIT :bye");

		Assert.Equal(":PrQuitter!PrQuitter@basil.test QUIT Quit", await watcher.ExpectFromAsync("QUIT", "PrQuitter"));
		var rest = await watcher.BarrierAsync();
		Assert.DoesNotContain(rest.Concat(watcher.Received), line => IrcClient.CommandOf(line) == "PART");
	}

	[Fact]
	public async Task UserWithAnIrcAndAnOsuConnection_IsShownJoiningOnce_AndNotLeavingWhenOneCloses()
	{
		await Host.CreateUserAsync("PrTwin");
		var key = await Host.CreateIrcKeyAsync("PrTwin");
		using var watcher = await Host.SignInAsync("PrTwinWatcher");
		using var osu = OsuClient.Create(Host, "PrTwin", "password");
		await osu.LoginAsync();
		await watcher.ExpectFromAsync("JOIN", "PrTwin");
		using var irc = await IrcClient.ConnectAsync(Host);
		await irc.RegisterAsync("PrTwin", key);
		await irc.ExpectFromAsync("366", "#osu");
		watcher.Clear();

		Host.Time.Advance(TimeSpan.FromSeconds(2));
		await osu.LogoutAsync();
		var seen = await watcher.BarrierAsync();

		Assert.DoesNotContain(seen, line => IrcClient.CommandOf(line) is "JOIN" or "PART" or "QUIT");
	}

	[Fact]
	public async Task ExtendedJoin_AddsTheAccountAndRealName()
	{
		using var watcher = await Host.SignInAsync("PrExtWatcher", "extended-join");
		using var osu = await Host.LoginOsuAsync("PrExtComes");

		Assert.Equal(":PrExtComes!PrExtComes@basil.test JOIN #osu PrExtComes PrExtComes",
			await watcher.ExpectFromAsync("JOIN", "PrExtComes"));
	}

	[Fact]
	public async Task AwayNotify_RelaysAwayChangesToClientsThatAskedForThem()
	{
		using var watcher = await Host.SignInAsync("PrAwayWatcher", "away-notify");
		using var sleeper = await Host.SignInAsync("PrSleeper");
		await watcher.BarrierAsync();

		await sleeper.SendAsync("AWAY :zzz");
		Assert.Equal(":PrSleeper!PrSleeper@basil.test AWAY :zzz", await watcher.ExpectAsync("AWAY"));
		await sleeper.SendAsync("AWAY");
		Assert.Equal(":PrSleeper!PrSleeper@basil.test AWAY", await watcher.ExpectAsync("AWAY"));
	}

	[Fact]
	public async Task AwayNotify_IsNotSentWithoutTheCapability()
	{
		using var watcher = await Host.SignInAsync("PrNoAwayCap");
		using var sleeper = await Host.SignInAsync("PrNoAwaySleeper");
		await watcher.BarrierAsync();

		await sleeper.SendAsync("AWAY :zzz");
		await sleeper.ExpectAsync("306");
		var seen = await watcher.BarrierAsync();

		Assert.DoesNotContain(seen, line => IrcClient.CommandOf(line) == "AWAY");
	}
}
