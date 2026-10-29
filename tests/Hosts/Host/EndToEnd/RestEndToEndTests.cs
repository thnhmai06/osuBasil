using System.Net;
using System.Text.Json.Nodes;
using Basil.Testing.Clients;

namespace Basil.Host.Tests.EndToEnd;

/// <summary>
///     The HTTP host end to end: signing in, reading the session, changing a room, watching the room and its match
///     report live, reconnecting after the last event, and watching a player through the API.
/// </summary>
[Collection("Host end to end")]
public sealed class RestEndToEndTests(HostEndToEndFixture fixture)
{
	private BasilHost Host => fixture.Host;

	[Fact]
	public async Task Login_Session_RoomCommands_AndTheLiveRoomStream()
	{
		var user = await Host.CreateUserAsync("E2eRestRooms");
		using var rest = new RestClient(Host.Url, Host.Domain);

		var id = await rest.LoginAsync("E2eRestRooms");
		var (sessionStatus, session) = await rest.CallAsync(HttpMethod.Get, "/v1/auth/session");
		var (created, room) = await rest.CallAsync(HttpMethod.Post, "/v1/rooms", new { name = "e2e rest room" });
		var roomId = (int)room!["data"]!["id"]!;
		var (locked, _) = await rest.CallAsync(HttpMethod.Post, $"/v1/rooms/{roomId}/lock");

		Assert.Equal(user.Id, id);
		Assert.Equal(200, sessionStatus);
		Assert.Equal(user.Id, (int)session!["data"]!["userId"]!);
		Assert.Equal(201, created);
		Assert.Equal(200, locked);

		var (response, reader) = await rest.OpenStreamAsync($"/v1/rooms/{roomId}/live");
		using (response)
		{
			Assert.NotNull(reader);
			var snapshot = await reader.ReadAsync();
			Assert.Equal("snapshot", snapshot!.Value.Event);
			Assert.True((bool)JsonNode.Parse(snapshot.Value.Data)!["locked"]!);

			var (renamed, _) = await rest.CallAsync(HttpMethod.Post, $"/v1/rooms/{roomId}/name", new { name = "renamed" });
			var bundle = await reader.NextAsync(Host);
			var (closed, _) = await rest.CallAsync(HttpMethod.Post, $"/v1/rooms/{roomId}/close");
			var closedEvent = await reader.NextAsync(Host);

			Assert.Equal(200, renamed);
			Assert.Equal("bundle", bundle.Event);
			Assert.Equal("renamed", (string)JsonNode.Parse(bundle.Data)![0]!["room"]!["name"]!);
			Assert.Equal(200, closed);
			Assert.Equal("closed", (string)JsonNode.Parse(closedEvent.Data)![0]!["type"]!);
			Assert.Null(await reader.ReadAsync());
		}
	}

	[Fact]
	public async Task TheMatchReport_IsPublicWhileRunning_And204_AnswersAReconnectAfterTheLastEvent()
	{
		await Host.CreateUserAsync("E2eRestMatches");
		using var owner = new RestClient(Host.Url, Host.Domain);
		await owner.LoginAsync("E2eRestMatches");
		var (_, room) = await owner.CallAsync(HttpMethod.Post, "/v1/rooms", new { name = "watched match" });
		var roomId = (int)room!["data"]!["id"]!;
		var matchId = (int)room["data"]!["matchId"]!;
		using var anon = new RestClient(Host.Url, Host.Domain);

		var (response, reader) = await anon.OpenStreamAsync($"/v1/matches/{matchId}");
		using (response)
		{
			Assert.Equal(HttpStatusCode.OK, response.StatusCode);
			Assert.NotNull(reader);
			var snapshot = await reader.ReadAsync();
			Assert.Equal("snapshot", snapshot!.Value.Event);
			Assert.Equal(matchId, (int)JsonNode.Parse(snapshot.Value.Data)!["match"]!["id"]!);

			await owner.CallAsync(HttpMethod.Post, $"/v1/rooms/{roomId}/close");
			while (await reader.NextOrEndAsync(Host) is not null)
			{
			}

			var lastId = reader.LastId;
			Assert.NotNull(lastId);
			var (again, none) = await anon.OpenStreamAsync($"/v1/matches/{matchId}", lastId);
			using (again)
			{
				Assert.Equal(204, (int)again.StatusCode);
				Assert.Null(none);
			}
		}
	}

	[Fact]
	public async Task Spectate_OverTheApi_SendsOneBundleForTheFramesOfTheSecond()
	{
		using var player = await Host.NewClientAsync("E2eSpecPlayer");
		await player.LoginAsync();
		await Host.CreateUserAsync("E2eSpecWatch");
		using var watcher = new RestClient(Host.Url, Host.Domain);
		await watcher.LoginAsync("E2eSpecWatch");

		var (response, reader) = await watcher.OpenStreamAsync($"/v1/users/{player.LoginCode}/spectate");
		using (response)
		{
			Assert.NotNull(reader);
			var snapshot = await reader.ReadAsync();
			Assert.Equal("snapshot", snapshot!.Value.Event);
			await player.WaitForAsync(OsuClient.In.SpectatorJoined);
			var frames = ClientPacketWriter.FrameBundle(0, 7, ClientPacketWriter.ScoreFrame(500, 999, 3));

			await player.SendAsync(writer => writer.Packet(OsuClient.Out.SpectateFrames, frames));
			var bundle = await reader.NextAsync(Host);

			Assert.Equal("bundle", bundle.Event);
			Assert.Single(JsonNode.Parse(bundle.Data)!.AsArray());
		}
	}

	[Fact]
	public async Task AProtectedRoute_WithoutAToken_Answers401()
	{
		using var anon = new RestClient(Host.Url, Host.Domain);

		var (status, envelope) = await anon.CallAsync(HttpMethod.Get, "/v1/users");

		Assert.Equal(401, status);
		Assert.Equal(401, (int)envelope!["code"]!);
		Assert.Equal("unauthorized", (string)envelope["errors"]!["code"]!);
	}
}
