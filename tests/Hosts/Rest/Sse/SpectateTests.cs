using System.Text.Json.Nodes;

namespace Basil.Host.Rest.Tests.Sse;

/// <summary>Watching a player in game through the API: snapshot first, one bundle per second, tied to the session.</summary>
[Collection("REST host")]
public sealed class SpectateTests(RestHostFixture fixture)
{
	private static readonly TimeSpan Tick = TimeSpan.FromSeconds(1);

	private async Task<(OsuClient Player, RestClient Watcher, int PlayerId)> StartAsync(string player, string watcher)
	{
		var osu = await fixture.Host.NewClientAsync(player);
		await osu.LoginAsync();
		var (rest, _) = await fixture.SignInAsync(watcher);
		return (osu, rest, osu.LoginCode);
	}

	private static string SpectateKey(int id)
	{
		return "spectate:" + id;
	}

	[Fact]
	public async Task Spectate_WithoutAToken_Answers401_AndOfflinePlayerAnswers409()
	{
		var offline = await fixture.Host.CreateUserAsync("SpecOffline");
		var (client, _) = await fixture.SignInAsync("SpecNoGame");
		using (client)
		using (var anon = new RestClient(fixture.Host.Url, fixture.Host.Domain))
		{
			var (denied, _) = await anon.CallAsync(HttpMethod.Get, $"/v1/users/{offline.Id}/spectate");
			var (conflict, envelope) = await client.CallAsync(HttpMethod.Get, $"/v1/users/{offline.Id}/spectate");
			var (unknown, _) = await client.CallAsync(HttpMethod.Get, "/v1/users/999999/spectate");

			Assert.Equal(401, denied);
			Assert.Equal(409, conflict);
			Assert.Equal("targetOffline", (string)envelope!["errors"]!["code"]!);
			Assert.Equal(404, unknown);
		}
	}

	[Fact]
	public async Task Spectate_SendsTheSnapshotFirst_ThenOneBundleAfterEachSecondWithFrames()
	{
		var (player, watcher, id) = await StartAsync("SpecPlayer", "SpecWatcher");
		using (player)
		using (watcher)
		{
			var (response, reader) = await watcher.OpenStreamAsync($"/v1/users/{id}/spectate");
			using (response)
			{
				Assert.NotNull(reader);
				var snapshot = await reader.ReadAsync();
				Assert.Equal("snapshot", snapshot!.Value.Event);
				Assert.Equal("SpecPlayer", (string)JsonNode.Parse(snapshot.Value.Data)!["user"]!["name"]!);
				await player.WaitForAsync(OsuClient.In.SpectatorJoined);

				var bundle = ClientPacketWriter.FrameBundle(0, 7, ClientPacketWriter.ScoreFrame(500, 999, 3));
				await player.SendAsync(writer => writer.Packet(OsuClient.Out.SpectateFrames, bundle));
				await Pending.WaitAsync(fixture.Host, SpectateKey(id));
				fixture.Host.Time.Advance(Tick);
				var next = await reader.ReadAsync();

				Assert.Equal("bundle", next!.Value.Event);
				Assert.Single(JsonNode.Parse(next.Value.Data)!.AsArray());
			}
		}
	}

	[Fact]
	public async Task Spectate_ASecondWithoutEvents_SendsOnlyAKeepAlive_AndRevokingTheTokenEndsTheStream()
	{
		var (player, watcher, id) = await StartAsync("SpecQuiet", "SpecRevoked");
		using (player)
		using (watcher)
		{
			var (response, reader) = await watcher.OpenStreamAsync($"/v1/users/{id}/spectate");
			using (response)
			{
				await reader!.ReadAsync();
				fixture.Host.Time.Advance(Tick);

				var (logout, _) = await watcher.CallAsync(HttpMethod.Post, "/v1/auth/logout");
				fixture.Host.Time.Advance(Tick);

				Assert.Equal(200, logout);
				Assert.Null(await reader.ReadAsync());
			}
		}
	}

	[Fact]
	public async Task Spectate_WhenThePlayerGoesOffline_TheStreamEnds()
	{
		var (player, watcher, id) = await StartAsync("SpecLeaver", "SpecStays");
		using (watcher)
		{
			var (response, reader) = await watcher.OpenStreamAsync($"/v1/users/{id}/spectate");
			using (response)
			{
				await reader!.ReadAsync();
				// A client that logs out within a second of logging in is ignored, as bancho.py does.
				fixture.Host.Time.Advance(TimeSpan.FromSeconds(2));
				await player.LogoutAsync();
				player.Dispose();

				Assert.Null(await reader.ReadAsync());
			}
		}
	}
}