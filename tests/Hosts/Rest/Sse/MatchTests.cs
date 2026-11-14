using System.Text.Json.Nodes;
using Basil.Domain.Users;

namespace Basil.Host.Rest.Tests.Sse;

/// <summary>Match reports: the manager list and the public live report.</summary>
[Collection("REST host")]
public sealed class MatchTests(RestHostFixture fixture)
{
	private static readonly UserPrivileges Manager = UserPrivilegesExtensions.Default | UserPrivileges.Tournament;

	private static async Task<(int RoomId, int MatchId)> OpenAsync(RestClient client, string name,
		bool isPrivate = false)
	{
		var (status, envelope) = await client.CallAsync(HttpMethod.Post, "/v1/rooms", new { name, isPrivate });
		Assert.Equal(201, status);
		return ((int)envelope!["data"]!["id"]!, (int)envelope["data"]!["matchId"]!);
	}

	[Fact]
	public async Task List_ManagersOnly()
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
			Assert.Equal("privilegeDenied", (string)envelope!["errors"]!["code"]!);
			Assert.Equal(200, ok);
			Assert.Equal(422, tooMany);
		}
	}

	[Fact]
	public async Task UnknownMatch_Answers404()
	{
		using var anon = new RestClient(fixture.Host.Url, fixture.Host.Domain);

		var (response, _) = await anon.OpenStreamAsync("/v1/matches/999999", ct: TestContext.Current.CancellationToken);
		using (response)
		{
			Assert.Equal(404, (int)response.StatusCode);
		}
	}

	[Fact]
	public async Task PrivateHidden_CreatorSees()
	{
		var (owner, _) = await fixture.SignInAsync("MatchSecret");
		using (owner)
		using (var anon = new RestClient(fixture.Host.Url, fixture.Host.Domain))
		{
			var (_, matchId) = await OpenAsync(owner, "Secret", true);

			var (hidden, _) =
				await anon.OpenStreamAsync($"/v1/matches/{matchId}", ct: TestContext.Current.CancellationToken);
			var (shown, reader) =
				await owner.OpenStreamAsync($"/v1/matches/{matchId}", ct: TestContext.Current.CancellationToken);
			using (hidden)
			using (shown)
			{
				Assert.Equal(404, (int)hidden.StatusCode);
				Assert.NotNull(reader);
			}
		}
	}

	[Fact]
	public async Task OpenMatch_FullThenNew()
	{
		var (owner, _) = await fixture.SignInAsync("MatchLive");
		var referee = await fixture.Host.CreateUserAsync("MatchLiveRef");
		using (owner)
		using (var anon = new RestClient(fixture.Host.Url, fixture.Host.Domain))
		{
			var (roomId, matchId) = await OpenAsync(owner, "Live");
			await fixture.Host.DrainEventsAsync();

			var (response, reader) =
				await anon.OpenStreamAsync($"/v1/matches/{matchId}", ct: TestContext.Current.CancellationToken);
			using (response)
			{
				Assert.NotNull(reader);
				var snapshot = await reader.ReadAsync(TestContext.Current.CancellationToken);
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
	public async Task EndedMatch_SnapshotEnds204()
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

			var (response, reader) =
				await anon.OpenStreamAsync($"/v1/matches/{matchId}", ct: TestContext.Current.CancellationToken);
			string lastId;
			using (response)
			{
				Assert.NotNull(reader);
				var snapshot = await reader.ReadAsync(TestContext.Current.CancellationToken);
				Assert.Equal("snapshot", snapshot!.Value.Event);
				Assert.Null(await reader.ReadAsync(TestContext.Current.CancellationToken));
				lastId = reader.LastId!;
			}

			var (again, none) =
				await anon.OpenStreamAsync($"/v1/matches/{matchId}", lastId, TestContext.Current.CancellationToken);
			using (again)
			{
				Assert.Equal(204, (int)again.StatusCode);
				Assert.Null(none);
			}
		}
	}
}