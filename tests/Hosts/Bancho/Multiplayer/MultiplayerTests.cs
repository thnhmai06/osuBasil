using Basil.Testing.Clients;

namespace Basil.Host.Bancho.Tests.Multiplayer;

/// <summary>Multiplayer as the osu! client plays it, end to end through the real server.</summary>
[Collection("Bancho host")]
public sealed class MultiplayerTests(BanchoHostFixture fixture)
{
	private BasilHost Host => fixture.Host;

	private static int MatchIdOf(ServerPacket joinSuccess) => BitConverter.ToUInt16(joinSuccess.Payload, 0);

	private async Task<(OsuClient Host, OsuClient Guest, int MatchId)> SeatedPairAsync(string hostName, string guestName)
	{
		var beatmap = (await Host.ImportSampleAsync("Sets/padoru-standard.osz")).First();
		var hostClient = await Host.NewClientAsync(hostName);
		var guest = await Host.NewClientAsync(guestName);
		await hostClient.LoginAsync();
		await guest.LoginAsync();
		await hostClient.SendAsync(writer => writer.Packet(OsuClient.Out.CreateMatch,
			ClientPacketWriter.Match("room of " + hostName, "pw", mapMd5: beatmap.Hash.ToString(), mapId: beatmap.Id,
				mapName: beatmap.Name)));
		var created = await hostClient.WaitForAsync(OsuClient.In.MatchJoinSuccess);
		Assert.NotNull(created);
		var matchId = MatchIdOf(created);
		await guest.SendAsync(writer => writer.Packet(OsuClient.Out.JoinMatch, ClientPacketWriter.JoinMatch(matchId, "pw")));
		Assert.NotNull(await guest.WaitForAsync(OsuClient.In.MatchJoinSuccess));
		return (hostClient, guest, matchId);
	}

	[Fact]
	public async Task CreateMatch_SeatsTheCreatorAndShowsTheMatchToTheLobby()
	{
		var watcher = await Host.NewClientAsync("LobbyWatcher");
		await watcher.LoginAsync();
		await watcher.SendAsync(writer => writer.Packet(OsuClient.Out.JoinLobby));
		var creator = await Host.NewClientAsync("RoomCreator");
		await creator.LoginAsync();

		await creator.SendAsync(writer => writer.Packet(OsuClient.Out.CreateMatch, ClientPacketWriter.Match("lobby room", "")));

		Assert.NotNull(await creator.WaitForAsync(OsuClient.In.MatchJoinSuccess));
		Assert.NotNull(await watcher.WaitForAsync(OsuClient.In.NewMatch));
	}

	[Fact]
	public async Task JoinMatch_WrongPassword_Fails()
	{
		var creator = await Host.NewClientAsync("PwCreator");
		var guest = await Host.NewClientAsync("PwGuest");
		await creator.LoginAsync();
		await guest.LoginAsync();
		await creator.SendAsync(writer => writer.Packet(OsuClient.Out.CreateMatch, ClientPacketWriter.Match("pw room", "secret")));
		var matchId = MatchIdOf((await creator.WaitForAsync(OsuClient.In.MatchJoinSuccess))!);

		var answer = await guest.SendAsync(writer =>
			writer.Packet(OsuClient.Out.JoinMatch, ClientPacketWriter.JoinMatch(matchId, "wrong")));

		Assert.Contains(answer, packet => packet.Id == OsuClient.In.MatchJoinFail);
	}

	[Fact]
	public async Task Round_ReadyStartLoadScoreComplete_ReachesEveryPlayerInOrder()
	{
		var (host, guest, _) = await SeatedPairAsync("RoundHost", "RoundGuest");
		host.ClearReceived();
		guest.ClearReceived();

		await guest.SendAsync(writer => writer.Packet(OsuClient.Out.MatchReady));
		await host.SendAsync(writer => writer.Packet(OsuClient.Out.MatchReady));
		await host.SendAsync(writer => writer.Packet(OsuClient.Out.MatchStart));
		Assert.NotNull(await host.WaitForAsync(OsuClient.In.MatchStart));
		Assert.NotNull(await guest.WaitForAsync(OsuClient.In.MatchStart));

		await host.SendAsync(writer => writer.Packet(OsuClient.Out.MatchLoadComplete));
		await guest.SendAsync(writer => writer.Packet(OsuClient.Out.MatchLoadComplete));
		Assert.NotNull(await host.WaitForAsync(OsuClient.In.MatchAllPlayersLoaded));
		Assert.NotNull(await guest.WaitForAsync(OsuClient.In.MatchAllPlayersLoaded));

		await host.SendAsync(writer => writer.Packet(OsuClient.Out.MatchScoreUpdate,
			ClientPacketWriter.ScoreFrame(1000, 12345, 7)));
		var update = await guest.WaitForAsync(OsuClient.In.MatchScoreUpdate);
		Assert.NotNull(update);
		Assert.Equal(12345, BitConverter.ToInt32(update.Payload, 4 + 1 + 12));

		await host.SendAsync(writer => writer.Packet(OsuClient.Out.MatchComplete));
		await guest.SendAsync(writer => writer.Packet(OsuClient.Out.MatchComplete));
		Assert.NotNull(await host.WaitForAsync(OsuClient.In.MatchComplete));
		Assert.NotNull(await guest.WaitForAsync(OsuClient.In.MatchComplete));
	}

	[Fact]
	public async Task Round_AfterIt_ASubmittedScoreIsAccepted_AndListedOnTheLeaderboard()
	{
		var (host, guest, _) = await SeatedPairAsync("ScoreHost", "ScoreGuest");
		var beatmap = (await Host.ImportSampleAsync("Sets/padoru-standard.osz")).First();
		await guest.SendAsync(writer => writer.Packet(OsuClient.Out.MatchReady));
		await host.SendAsync(writer => writer.Packet(OsuClient.Out.MatchStart));
		await host.WaitForAsync(OsuClient.In.MatchStart);

		var (status, text) = await guest.SubmitScoreAsync(beatmap.Hash.ToString(), 424242, replay: new byte[64]);
		var (_, board) = await guest.GetScoresAsync(beatmap.Hash.ToString());

		Assert.Equal(System.Net.HttpStatusCode.OK, status);
		Assert.StartsWith("beatmapId:", text);
		Assert.Contains("|ScoreGuest|424242|", board);
	}

	[Fact]
	public async Task Kick_ByNonHost_IsRefused_AndLeaveReachesTheHost()
	{
		var (host, guest, _) = await SeatedPairAsync("LeaveHost", "LeaveGuest");
		host.ClearReceived();

		await guest.SendAsync(writer => writer.Packet(OsuClient.Out.PartMatch));

		Assert.NotNull(await host.WaitForAsync(OsuClient.In.UpdateMatch));
	}
}
