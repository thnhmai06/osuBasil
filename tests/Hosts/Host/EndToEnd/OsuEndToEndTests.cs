using System.Net;
using Basil.Testing.Clients;

namespace Basil.Host.Tests.EndToEnd;

/// <summary>
///     The osu! host end to end: a client logs in, opens a room, plays a round, submits a score, and is signed out
///     from the HTTP API, after which its next automatic login asks for the password once.
/// </summary>
[Collection("Host end to end")]
public sealed class OsuEndToEndTests(HostEndToEndFixture fixture)
{
	private BasilHost Host => fixture.Host;

	private static int MatchIdOf(ServerPacket joinSuccess)
	{
		return BitConverter.ToUInt16(joinSuccess.Payload, 0);
	}

	[Fact]
	public async Task Login_JoinRoom_Play_SubmitScore_AndRemoteSignOut_MakesTheNextLoginAskForThePasswordOnce()
	{
		var beatmap = (await Host.ImportSampleAsync("Sets/padoru-standard.osz")).First();
		using var player = await Host.NewClientAsync("E2eOsuPlayer");
		using var guest = await Host.NewClientAsync("E2eOsuGuest");
		await player.LoginAsync();
		await guest.LoginAsync();
		Assert.True(player.LoginCode > 0);

		await player.SendAsync(writer => writer.Packet(OsuClient.Out.CreateMatch,
			ClientPacketWriter.Match("e2e room", "", mapMd5: beatmap.Hash.ToString(), mapId: beatmap.Id,
				mapName: beatmap.Name)));
		var created = await player.WaitForAsync(OsuClient.In.MatchJoinSuccess);
		var matchId = MatchIdOf(created!);
		await guest.SendAsync(writer =>
			writer.Packet(OsuClient.Out.JoinMatch, ClientPacketWriter.JoinMatch(matchId, "")));
		Assert.NotNull(await guest.WaitForAsync(OsuClient.In.MatchJoinSuccess));

		await guest.SendAsync(writer => writer.Packet(OsuClient.Out.MatchReady));
		await player.SendAsync(writer => writer.Packet(OsuClient.Out.MatchStart));
		Assert.NotNull(await player.WaitForAsync(OsuClient.In.MatchStart));
		await player.SendAsync(writer => writer.Packet(OsuClient.Out.MatchLoadComplete));
		await guest.SendAsync(writer => writer.Packet(OsuClient.Out.MatchLoadComplete));
		Assert.NotNull(await player.WaitForAsync(OsuClient.In.MatchAllPlayersLoaded));
		await player.SendAsync(writer => writer.Packet(OsuClient.Out.MatchComplete));
		await guest.SendAsync(writer => writer.Packet(OsuClient.Out.MatchComplete));
		Assert.NotNull(await player.WaitForAsync(OsuClient.In.MatchComplete));

		var (submitted, reply) = await player.SubmitScoreAsync(beatmap.Hash.ToString(), 4242, replay: new byte[64]);
		var (_, board) = await player.GetScoresAsync(beatmap.Hash.ToString());

		Assert.Equal(HttpStatusCode.OK, submitted);
		Assert.StartsWith("beatmapId:", reply);
		Assert.Contains("|E2eOsuPlayer|4242|", board);

		// Signing the client out from the API marks the device: the next login asks for the password once.
		Host.Time.Advance(TimeSpan.FromSeconds(2));
		using var rest = new RestClient(Host.Url, Host.Domain);
		var playerId = await rest.LoginAsync("E2eOsuPlayer");
		var (_, sessions) = await rest.CallAsync(HttpMethod.Get, $"/v1/users/{playerId}/sessions?state=online");
		var osuSession = sessions!["data"]!.AsArray().Single(session => (string)session!["type"]! == "osu");
		var (closed, _) = await rest.CallAsync(HttpMethod.Delete,
			$"/v1/users/{playerId}/sessions/{(Guid)osuSession!["id"]!}");

		await player.LoginAsync();
		var refusedOnce = player.LoginCode;
		await player.LoginAsync();

		Assert.Equal(200, closed);
		Assert.Equal(-7, refusedOnce);
		Assert.True(player.LoginCode > 0);
	}
}