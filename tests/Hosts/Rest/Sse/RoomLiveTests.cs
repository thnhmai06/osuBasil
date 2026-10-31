using System.Text.Json.Nodes;

namespace Basil.Host.Rest.Tests.Sse;

/// <summary>The live stream of a room.</summary>
[Collection("REST host")]
public sealed class RoomLiveTests(RestHostFixture fixture)
{
	[Fact]
	public async Task Live_NeedsAToken_AndAHiddenRoomIs404()
	{
		var (owner, _) = await fixture.SignInAsync("LiveOwner");
		var (other, _) = await fixture.SignInAsync("LiveOther");
		using (owner)
		using (other)
		using (var anon = new RestClient(fixture.Host.Url, fixture.Host.Domain))
		{
			var (_, envelope) =
				await owner.CallAsync(HttpMethod.Post, "/v1/rooms", new { name = "Hidden", isPrivate = true });
			var roomId = (int)envelope!["data"]!["id"]!;

			var (noToken, _) = await anon.OpenStreamAsync($"/v1/rooms/{roomId}/live");
			var (hidden, _) = await other.OpenStreamAsync($"/v1/rooms/{roomId}/live");
			using (noToken)
			using (hidden)
			{
				Assert.Equal(401, (int)noToken.StatusCode);
				Assert.Equal(404, (int)hidden.StatusCode);
			}
		}
	}

	[Fact]
	public async Task Live_SendsTheRoomFirst_ThenTheChange_AndEndsWhenTheRoomCloses()
	{
		var (owner, _) = await fixture.SignInAsync("LiveWatch");
		using (owner)
		{
			var (_, envelope) = await owner.CallAsync(HttpMethod.Post, "/v1/rooms", new { name = "Watched" });
			var roomId = (int)envelope!["data"]!["id"]!;
			var (response, reader) = await owner.OpenStreamAsync($"/v1/rooms/{roomId}/live");
			using (response)
			{
				Assert.NotNull(reader);
				var snapshot = await reader.ReadAsync();
				Assert.Equal("snapshot", snapshot!.Value.Event);
				Assert.False((bool)JsonNode.Parse(snapshot.Value.Data)!["locked"]!);

				await owner.CallAsync(HttpMethod.Post, $"/v1/rooms/{roomId}/lock");
				var bundle = await reader.NextBundleAsync(fixture.Host);
				Assert.True((bool)JsonNode.Parse(bundle.Data)![0]!["room"]!["locked"]!);

				await owner.CallAsync(HttpMethod.Post, $"/v1/rooms/{roomId}/close");
				var closed = await reader.NextBundleAsync(fixture.Host);
				Assert.Equal("closed", (string)JsonNode.Parse(closed.Data)![0]!["type"]!);
				Assert.Null(await reader.ReadAsync());
			}
		}
	}
}