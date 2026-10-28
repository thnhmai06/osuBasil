using Basil.Domain.Users;
using Basil.Testing.Clients;

namespace Basil.Host.Bancho.Tests.Multiplayer;

/// <summary>The osu!tourney client observes a room without playing in it.</summary>
[Collection("Bancho host")]
public sealed class TourneyTests(BanchoHostFixture fixture)
{
	private BasilHost Host => fixture.Host;

	[Fact]
	public async Task TourneyClient_WithoutObservePermission_CannotLogin()
	{
		using var tourney = await Host.NewClientAsync("NoObserve", tourney: true);

		await tourney.LoginAsync();

		Assert.Equal(-1, tourney.LoginCode);
	}

	[Fact]
	public async Task TourneyClient_Observing_GetsTheMatchAndItsRounds()
	{
		var creator = await Host.NewClientAsync("TourneyHost");
		var player = await Host.NewClientAsync("TourneyPlayer");
		var observerUser = await Host.NewClientAsync("TourneyWatch", permissions: UserPermissionsGroups.Default | UserPermissions.RoomObserve,
			tourney: true);
		await creator.LoginAsync();
		await player.LoginAsync();
		await observerUser.LoginAsync();
		Assert.True(observerUser.LoginCode > 0);
		await creator.SendAsync(writer => writer.Packet(OsuClient.Out.CreateMatch, ClientPacketWriter.Match("tourney room", "")));
		var matchId = BitConverter.ToUInt16((await creator.WaitForAsync(OsuClient.In.MatchJoinSuccess))!.Payload, 0);

		await observerUser.SendAsync(writer => writer.Int(OsuClient.Out.TournamentMatchInfoRequest, matchId));
		Assert.NotNull(await observerUser.WaitForAsync(OsuClient.In.UpdateMatch));
		observerUser.ClearReceived();
		await observerUser.SendAsync(writer => writer.Int(OsuClient.Out.TournamentJoinMatchChannel, matchId));
		await player.SendAsync(writer => writer.Packet(OsuClient.Out.JoinMatch, ClientPacketWriter.JoinMatch(matchId, "")));
		await player.WaitForAsync(OsuClient.In.MatchJoinSuccess);

		Assert.NotNull(await observerUser.WaitForAsync(OsuClient.In.UpdateMatch));
	}
}
