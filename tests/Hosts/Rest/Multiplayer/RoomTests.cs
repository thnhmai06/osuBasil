namespace Basil.Host.Rest.Tests.Multiplayer;

/// <summary>Rooms and the room command routes (ADR-009): permissions, visibility and outcomes.</summary>
[Collection("REST host")]
public sealed class RoomTests(RestHostFixture fixture)
{
	private static async Task<int> OpenAsync(RestClient client, string name, bool isPrivate = false)
	{
		var (status, envelope) = await client.CallAsync(HttpMethod.Post, "/v1/rooms", new { name, isPrivate });
		Assert.Equal(201, status);
		return (int)envelope!["data"]!["id"]!;
	}

	[Fact]
	public async Task Rooms_WithoutAToken_Answer401()
	{
		using var client = new RestClient(fixture.Host.Url, fixture.Host.Domain);

		var (list, _) = await client.CallAsync(HttpMethod.Get, "/v1/rooms");
		var (start, _) = await client.CallAsync(HttpMethod.Post, "/v1/rooms/1/start");

		Assert.Equal(401, list);
		Assert.Equal(401, start);
	}

	[Fact]
	public async Task Create_ThenGet_ShowsTheCreator_AndNeverThePassword()
	{
		var (client, id) = await fixture.SignInAsync("RoomMaker");
		using (client)
		{
			var (created, envelope) = await client.CallAsync(HttpMethod.Post, "/v1/rooms",
				new { name = "Finals", password = "secret" });
			var room = (int)envelope!["data"]!["id"]!;
			var (status, got) = await client.CallAsync(HttpMethod.Get, $"/v1/rooms/{room}");

			Assert.Equal(201, created);
			Assert.Equal(200, status);
			Assert.Equal(id, (int)got!["data"]!["creator"]!["id"]!);
			Assert.True((bool)got["data"]!["hasPassword"]!);
			Assert.DoesNotContain("secret", got.ToJsonString());
		}
	}

	[Fact]
	public async Task Create_WithoutAName_Answers422()
	{
		var (client, _) = await fixture.SignInAsync("RoomNoName");
		using (client)
		{
			var (status, _) = await client.CallAsync(HttpMethod.Post, "/v1/rooms", new { name = " " });

			Assert.Equal(422, status);
		}
	}

	[Fact]
	public async Task Commands_ByTheCreator_ChangeTheRoom()
	{
		var (client, _) = await fixture.SignInAsync("RoomCmd");
		using (client)
		{
			var room = await OpenAsync(client, "Cmd");

			var (name, _) = await client.CallAsync(HttpMethod.Post, $"/v1/rooms/{room}/name", new { name = "Renamed" });
			var (size, _) = await client.CallAsync(HttpMethod.Post, $"/v1/rooms/{room}/size", new { size = 8 });
			var (priv, _) =
				await client.CallAsync(HttpMethod.Post, $"/v1/rooms/{room}/private", new { @private = true });
			var (locked, _) = await client.CallAsync(HttpMethod.Post, $"/v1/rooms/{room}/lock");
			var (_, view) = await client.CallAsync(HttpMethod.Get, $"/v1/rooms/{room}");

			Assert.Equal([200, 200, 200, 200], [name, size, priv, locked]);
			Assert.Equal("Renamed", (string)view!["data"]!["name"]!);
			Assert.True((bool)view["data"]!["isPrivate"]!);
			Assert.True((bool)view["data"]!["locked"]!);
		}
	}

	[Theory]
	[InlineData("size", 0)]
	[InlineData("size", 17)]
	public async Task Size_OutOfRange_Answers422(string command, int size)
	{
		var (client, _) = await fixture.SignInAsync("RoomSize" + size);
		using (client)
		{
			var room = await OpenAsync(client, "Size");

			var (status, _) = await client.CallAsync(HttpMethod.Post, $"/v1/rooms/{room}/{command}", new { size });

			Assert.Equal(422, status);
		}
	}

	[Fact]
	public async Task Command_ByANonManager_Answers403_AndAPrivateRoomIsHidden()
	{
		var (owner, _) = await fixture.SignInAsync("RoomOwner");
		var (other, _) = await fixture.SignInAsync("RoomOther");
		using (owner)
		using (other)
		{
			var open = await OpenAsync(owner, "Open");
			var hidden = await OpenAsync(owner, "Hidden", true);

			var (denied, _) = await other.CallAsync(HttpMethod.Post, $"/v1/rooms/{open}/lock");
			var (notFound, _) = await other.CallAsync(HttpMethod.Get, $"/v1/rooms/{hidden}");
			var (_, list) = await other.CallAsync(HttpMethod.Get, "/v1/rooms?limit=100");

			Assert.Equal(403, denied);
			Assert.Equal(404, notFound);
			Assert.DoesNotContain(list!["data"]!.AsArray(), room => (int)room!["id"]! == hidden);
			Assert.Contains(list["data"]!.AsArray(), room => (int)room!["id"]! == open);
		}
	}

	[Fact]
	public async Task Close_RemovesTheRoom_AndLaterCommandsAnswer404()
	{
		var (client, _) = await fixture.SignInAsync("RoomCloser");
		using (client)
		{
			var room = await OpenAsync(client, "Closing");

			var (closed, _) = await client.CallAsync(HttpMethod.Post, $"/v1/rooms/{room}/close");
			var (after, _) = await client.CallAsync(HttpMethod.Post, $"/v1/rooms/{room}/lock");

			Assert.Equal(200, closed);
			Assert.Equal(404, after);
		}
	}

	[Fact]
	public async Task Kick_OfAUserWhoIsNotInTheRoom_Answers409()
	{
		var (client, _) = await fixture.SignInAsync("RoomKicker");
		var target = await fixture.Host.CreateUserAsync("RoomKickee");
		using (client)
		{
			var room = await OpenAsync(client, "Kick");

			var (status, _) =
				await client.CallAsync(HttpMethod.Post, $"/v1/rooms/{room}/kick", new { user = target.Id });

			Assert.Equal(409, status);
		}
	}

	[Fact]
	public async Task Timer_OutOfRange_Answers422_AndReferees_CanBeAddedAndRemoved()
	{
		var (client, _) = await fixture.SignInAsync("RoomTimer");
		var referee = await fixture.Host.CreateUserAsync("RoomRef");
		using (client)
		{
			var room = await OpenAsync(client, "Timer");

			var (bad, _) = await client.CallAsync(HttpMethod.Post, $"/v1/rooms/{room}/timer", new { seconds = 100000 });
			var (added, _) =
				await client.CallAsync(HttpMethod.Post, $"/v1/rooms/{room}/addref", new { user = referee.Id });
			var (_, view) = await client.CallAsync(HttpMethod.Get, $"/v1/rooms/{room}");
			var (removed, _) =
				await client.CallAsync(HttpMethod.Post, $"/v1/rooms/{room}/removeref", new { user = referee.Id });

			Assert.Equal(422, bad);
			Assert.Equal(200, added);
			Assert.Single(view!["data"]!["referees"]!.AsArray());
			Assert.Equal(200, removed);
		}
	}

	[Fact]
	public async Task List_WithAPageOutOfBounds_Answers422()
	{
		var (client, _) = await fixture.SignInAsync("RoomPaging");
		using (client)
		{
			var (status, _) = await client.CallAsync(HttpMethod.Get, "/v1/rooms?limit=101");

			Assert.Equal(422, status);
		}
	}
}