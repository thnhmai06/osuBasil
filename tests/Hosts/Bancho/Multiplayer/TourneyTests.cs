using Basil.Application.Multiplayer.Runtime;
using Basil.Application.Users.Connections;
using Basil.Application.Users.Users;
using Basil.Domain.Users;
using Microsoft.Extensions.DependencyInjection;
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

	private async Task<(int MatchId, Connection Observer)> PrivateRoomAndTourneyAsync(string suffix, UserPermissions tourneyPermissions)
	{
		var creator = await Host.NewClientAsync("PrivHost" + suffix);
		var tourney = await Host.NewClientAsync("PrivWatch" + suffix, permissions: tourneyPermissions, tourney: true);
		await creator.LoginAsync();
		await tourney.LoginAsync();
		Assert.True(tourney.LoginCode > 0);
		await creator.SendAsync(writer => writer.Packet(OsuClient.Out.CreateMatch, ClientPacketWriter.Match("private room", "")));
		var matchId = BitConverter.ToUInt16((await creator.WaitForAsync(OsuClient.In.MatchJoinSuccess))!.Payload, 0);
		var registry = Host.Services.GetRequiredService<UserRegistry>();
		var room = Host.Services.GetRequiredService<Lobby>().Find(matchId)!;
		var outcome = await room.ScopeTo(registry.Find(creator.Token!)!).Settings.ConfigureAsync(new RoomSettingsChange(IsPrivate: true));
		Assert.True(outcome.IsOk);
		await tourney.SendAsync(writer => writer.Int(OsuClient.Out.TournamentJoinMatchChannel, matchId));
		return (matchId, registry.Find(tourney.Token!)!);
	}

	[Fact]
	public async Task TourneyClient_JoiningAPrivateRoomItMayNotView_DoesNotBecomeAnObserver()
	{
		var (matchId, observer) = await PrivateRoomAndTourneyAsync("No", UserPermissionsGroups.Default | UserPermissions.RoomObserve);

		Assert.DoesNotContain(observer, Host.Services.GetRequiredService<Lobby>().Find(matchId)!.Members.Observers);
	}

	[Fact]
	public async Task TourneyClient_JoiningAPrivateRoomItMayView_BecomesAnObserver()
	{
		var (matchId, observer) = await PrivateRoomAndTourneyAsync("Yes",
			UserPermissionsGroups.Default | UserPermissions.RoomObserve | UserPermissions.RoomManageViewPrivate);

		Assert.Contains(observer, Host.Services.GetRequiredService<Lobby>().Find(matchId)!.Members.Observers);
	}
}
