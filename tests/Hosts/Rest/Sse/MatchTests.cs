using System.Text.Json.Nodes;
using Basil.Domain.Users;

namespace Basil.Host.Rest.Tests.Sse;

/// <summary>Match reports: the manager list and the public live report.</summary>
[Collection("REST host")]
public sealed class MatchTests(RestHostFixture fixture)
{
	private static readonly UserPermissions Manager = UserPermissionsGroups.Default | UserPermissions.RoomManageAny;

	private static async Task<(int RoomId, int MatchId)> OpenAsync(RestClient client, string name, bool isPrivate = false)
	{
		var (status, envelope) = await client.CallAsync(HttpMethod.Post, "/v1/rooms", new { name, isPrivate });
		Assert.Equal(201, status);
		return ((int)envelope!["data"]!["id"]!, (int)envelope["data"]!["matchId"]!);
	}

	[Fact]
	public async Task List_IsForManagersOnly()
	{
		var (plain, _) = await fixture.SignInAsync("MatchPlain");
		var (manager, _) = await fixture.SignInAsync("MatchManager", Manager);
		using (plain)
		using (manager)
		using (var anon = new RestClient(fixture.Host.Url, fixture.Host.Domain))
		{
			var (noToken, _) = await anon.CallAsync(HttpMethod.Get, "/v1/matches");
			var (denied, envelope) = await plain.CallAsync(HttpMethod.Get, "/v1/matches");
			var (ok, _) = await manager.CallAsync(HttpMethod.Get, "/v1/matches?limit=100");
			var (tooMany, _) = await manager.CallAsync(HttpMethod.Get, "/v1/matches?limit=101");

			Assert.Equal(401, noToken);
			Assert.Equal(403, denied);
			Assert.Equal((ulong)UserPermissions.RoomManageAny, (ulong)envelope!["errors"]!["missing"]!);
			Assert.Equal(200, ok);
			Assert.Equal(422, tooMany);
		}
	}

	[Fact]
	public async Task Report_OfAnUnknownMatch_Answers404()
	{
		using var anon = new RestClient(fixture.Host.Url, fixture.Host.Domain);

		var (response, _) = await anon.OpenStreamAsync("/v1/matches/999999");
		using (response)
			Assert.Equal(404, (int)response.StatusCode);
	}

	[Fact]
	public async Task Report_OfAPrivateMatch_IsHiddenFromAnonymousCallers_AndShownToItsCreator()
	{
		var (owner, _) = await fixture.SignInAsync("MatchSecret");
		using (owner)
		using (var anon = new RestClient(fixture.Host.Url, fixture.Host.Domain))
		{
			var (_, matchId) = await OpenAsync(owner, "Secret", true);

			var (hidden, _) = await anon.OpenStreamAsync($"/v1/matches/{matchId}");
			var (shown, reader) = await owner.OpenStreamAsync($"/v1/matches/{matchId}");
			using (hidden)
			using (shown)
			{
				Assert.Equal(404, (int)hidden.StatusCode);
				Assert.NotNull(reader);
			}
		}
	}

	[Fact]
	public async Task Report_OfAnOpenMatch_IsPublic_StartsWithTheFullReport_AndThenSendsNewEntries()
	{
		var (owner, _) = await fixture.SignInAsync("MatchLive");
		var referee = await fixture.Host.CreateUserAsync("MatchLiveRef");
		using (owner)
		using (var anon = new RestClient(fixture.Host.Url, fixture.Host.Domain))
		{
			var (roomId, matchId) = await OpenAsync(owner, "Live");
			await fixture.Host.DrainEventsAsync();

			var (response, reader) = await anon.OpenStreamAsync($"/v1/matches/{matchId}");
			using (response)
			{
				Assert.NotNull(reader);
				var snapshot = await reader.ReadAsync();
				Assert.Equal("snapshot", snapshot!.Value.Event);
				var report = JsonNode.Parse(snapshot.Value.Data)!;
				Assert.Equal(matchId, (int)report["match"]!["id"]!);
				var before = report["entries"]!.AsArray().Count;

				await owner.CallAsync(HttpMethod.Post, $"/v1/rooms/{roomId}/addref", new { user = referee.Id });
				var bundle = await reader.NextBundleAsync(fixture.Host);

				Assert.Equal("bundle", bundle.Event);
				var entry = JsonNode.Parse(bundle.Data)![0]!;
				Assert.Equal("event", (string)entry["type"]!);
				Assert.Equal("refAdded", (string)entry["eventType"]!);
				Assert.Equal(before.ToString(), reader.LastId);
			}
		}
	}

	[Fact]
	public async Task Report_OfAnEndedMatch_IsASnapshotThatEnds_AndTheLastEventIdAnswers204()
	{
		var (owner, _) = await fixture.SignInAsync("MatchEnded");
		using (owner)
		using (var anon = new RestClient(fixture.Host.Url, fixture.Host.Domain))
		{
			var (roomId, matchId) = await OpenAsync(owner, "Ended");
			await owner.CallAsync(HttpMethod.Post, $"/v1/rooms/{roomId}/close");
			await fixture.Host.DrainEventsAsync();
			while (true)
			{
				var (_, list) = await owner.CallAsync(HttpMethod.Get, "/v1/rooms?limit=100");
				if (list!["data"]!.AsArray().All(r => (int)r!["id"]! != roomId)) break;
			}

			var (response, reader) = await anon.OpenStreamAsync($"/v1/matches/{matchId}");
			string lastId;
			using (response)
			{
				Assert.NotNull(reader);
				var snapshot = await reader.ReadAsync();
				Assert.Equal("snapshot", snapshot!.Value.Event);
				Assert.Null(await reader.ReadAsync());
				lastId = reader.LastId!;
			}

			var (again, none) = await anon.OpenStreamAsync($"/v1/matches/{matchId}", lastId);
			using (again)
			{
				Assert.Equal(204, (int)again.StatusCode);
				Assert.Null(none);
			}
		}
	}
}
