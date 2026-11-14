using Basil.Application.Multiplayer.Objects;
using Basil.Application.Multiplayer.Registry;
using Basil.Application.Users.Objects.Connections;
using Basil.Application.Users.Registry;
using Basil.Domain.Users;
using Basil.Testing.Clients;
using Microsoft.Extensions.DependencyInjection;

namespace Basil.Host.Stable.Tests.Multiplayer;

/// <summary>The osu!tourney client observes a room without playing in it.</summary>
[Collection("Stable host")]
public sealed class TourneyTests(StableHostFixture fixture)
{
	private BasilHost Host => fixture.Host;

	[Fact]
	public async Task TourneyNoObserve_LoginDenied()
	{
		using var tourney = await Host.NewClientAsync("NoObserve", tourney: true);

		await tourney.LoginAsync();

		Assert.Equal(-1, tourney.LoginCode);
	}

	[Fact]
	public async Task TourneyObserving_MatchAndRounds()
	{
		var creator = await Host.NewClientAsync("TourneyHost");
		var player = await Host.NewClientAsync("TourneyPlayer");
		var observerUser = await Host.NewClientAsync("TourneyWatch",
			privileges: UserPrivilegesExtensions.Default | UserPrivileges.Tournament,
			tourney: true);
		await creator.LoginAsync();
		await player.LoginAsync();
		await observerUser.LoginAsync();
		Assert.True(observerUser.LoginCode > 0);
		await creator.SendAsync(writer =>
			writer.Packet(OsuClient.Out.CreateMatch, ClientPacketWriter.Match("tourney room", "")));
		var matchId = BitConverter.ToUInt16((await creator.WaitForAsync(OsuClient.In.MatchJoinSuccess))!.Payload, 0);

		await observerUser.SendAsync(writer => writer.Int(OsuClient.Out.TournamentMatchInfoRequest, matchId));
		Assert.NotNull(await observerUser.WaitForAsync(OsuClient.In.UpdateMatch));
		observerUser.ClearReceived();
		await observerUser.SendAsync(writer => writer.Int(OsuClient.Out.TournamentJoinMatchChannel, matchId));
		await player.SendAsync(writer =>
			writer.Packet(OsuClient.Out.JoinMatch, ClientPacketWriter.JoinMatch(matchId, "")));
		await player.WaitForAsync(OsuClient.In.MatchJoinSuccess);

		Assert.NotNull(await observerUser.WaitForAsync(OsuClient.In.UpdateMatch));
	}

	private async Task<(int MatchId, Connection Observer)> PrivateRoomAndTourneyAsync(string suffix,
		UserPrivileges tourneyPrivileges)
	{
		var creator = await Host.NewClientAsync("PrivHost" + suffix);
		var tourney = await Host.NewClientAsync("PrivWatch" + suffix, privileges: tourneyPrivileges, tourney: true);
		await creator.LoginAsync();
		await tourney.LoginAsync();
		Assert.True(tourney.LoginCode > 0);
		await creator.SendAsync(writer =>
			writer.Packet(OsuClient.Out.CreateMatch, ClientPacketWriter.Match("private room", "")));
		var matchId = BitConverter.ToUInt16((await creator.WaitForAsync(OsuClient.In.MatchJoinSuccess))!.Payload, 0);
		var registry = Host.Services.GetRequiredService<UserRegistry>();
		var room = Host.Services.GetRequiredService<Lobby>().Find(matchId)!;
		var outcome = await room.ScopeTo(registry.Find(creator.Token!)!).Settings
			.ConfigureAsync(new RoomSettingsChange(IsPrivate: true));
		Assert.True(outcome == RoomOutcome.Ok);
		await tourney.SendAsync(writer => writer.Int(OsuClient.Out.TournamentJoinMatchChannel, matchId));
		return (matchId, registry.Find(tourney.Token!)!);
	}

	[Fact]
	public async Task TourneyJoinsViewable_BecomesObserver()
	{
		var (matchId, observer) = await PrivateRoomAndTourneyAsync("Yes",
			UserPrivilegesExtensions.Default | UserPrivileges.Tournament | UserPrivileges.Tournament);

		Assert.Contains(observer, Host.Services.GetRequiredService<Lobby>().Find(matchId)!.Members.Observers);
	}
}