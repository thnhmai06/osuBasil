using Basil.Application.Chat.Runtime;
using Basil.Application.Multiplayer.Runtime;
using Basil.Domain.Users;
using Microsoft.Extensions.DependencyInjection;

namespace Basil.Host.Cli.Tests.Commands;

/// <summary>The <c>channels</c>, <c>rooms</c> and <c>matches</c> commands.</summary>
[Collection("CLI host")]
public sealed class RoomChannelCommandTests(CliFixture fixture)
{
	private Lobby Lobby => fixture.Host.Services.GetRequiredService<Lobby>();

	private async Task<int> OpenRoomAsync(string name)
	{
		var (code, text) = await fixture.RunAsync($"rooms create --name {name}");
		Assert.Equal(0, code);
		return int.Parse(text.Split(' ')[^1].TrimEnd('.'));
	}

	[Fact]
	public async Task Channels_CreateSetListDelete_ChangeTheGeneralChannels()
	{
		var name = "#" + CliFixture.Name("c");
		var registry = fixture.Host.Services.GetRequiredService<GeneralChannelRegistry>();

		var (created, _) = await fixture.RunAsync($"channels create --name {name} --topic first --autoJoin");
		var (set, _) = await fixture.RunAsync($"channels {name} set --topic second --visible false");
		var (_, listed) = await fixture.RunAsync("channels list");
		var value = registry.Find(name)!.Record.Value;
		var (deleted, _) = await fixture.RunAsync($"channels {name} delete");

		Assert.Equal(0, created);
		Assert.Equal(0, set);
		Assert.Contains(name, listed);
		Assert.Equal("second", value.Topic);
		Assert.False(value.Visible);
		Assert.True(value.AutoJoin);
		Assert.Equal(0, deleted);
		Assert.Null(registry.Find(name));
	}

	[Fact]
	public async Task Channels_PrivilegeMasksAreNumbers()
	{
		var name = "#" + CliFixture.Name("p");

		await fixture.RunAsync($"channels create --name {name} --readPrivileges 1 --writePrivileges 2");

		var value = fixture.Host.Services.GetRequiredService<GeneralChannelRegistry>().Find(name)!.Record.Value;
		Assert.Equal((UserPrivileges)1, value.ReadPrivileges);
		Assert.Equal((UserPrivileges)2, value.WritePrivileges);
	}

	[Fact]
	public async Task Channels_AnUnknownChannel_IsNotFound_AndAnInvalidNameIsInvalid()
	{
		Assert.Equal(1, (await fixture.RunAsync("channels #nothing-here set --topic x")).Code);
		Assert.Equal(1, (await fixture.RunAsync("channels #nothing-here delete")).Code);
		Assert.NotEqual(0, (await fixture.RunAsync("channels create --name no-hash")).Code);
	}

	[Fact]
	public async Task Rooms_Create_OpensATournamentRoomOfTheOperatorAndShowsIt()
	{
		var name = CliFixture.Name("room");

		var id = await OpenRoomAsync(name);
		var (code, text) = await fixture.RunAsync($"rooms {id}");
		var (_, listed) = await fixture.RunAsync("rooms list");

		Assert.Equal(0, code);
		Assert.True(Lobby.Rooms[id].IsTournament);
		Assert.Contains(name, text);
		Assert.Contains("creator:", text);
		Assert.Contains("BasilBot", text);
		Assert.Contains(name, listed);
	}

	[Fact]
	public async Task Rooms_LockAndUnlock_ChangeTheRoom()
	{
		var id = await OpenRoomAsync(CliFixture.Name("lock"));

		await fixture.RunAsync($"rooms {id} lock");
		var locked = Lobby.Rooms[id].Slots.Locked;
		await fixture.RunAsync($"rooms {id} unlock");

		Assert.True(locked);
		Assert.False(Lobby.Rooms[id].Slots.Locked);
	}

	[Fact]
	public async Task Rooms_NamePasswordPrivateSizeSet_ChangeTheSettings()
	{
		var id = await OpenRoomAsync(CliFixture.Name("set"));
		var rename = CliFixture.Name("renamed");

		var results = new[]
		{
			(await fixture.RunAsync($"rooms {id} name --name {rename}")).Code,
			(await fixture.RunAsync($"rooms {id} password --password hunter2")).Code,
			(await fixture.RunAsync($"rooms {id} private --private true")).Code,
			(await fixture.RunAsync($"rooms {id} size --size 4")).Code,
			(await fixture.RunAsync($"rooms {id} set --teamType teamVs --winCondition accuracy")).Code
		};

		var room = Lobby.Rooms[id];
		Assert.All(results, code => Assert.Equal(0, code));
		Assert.Equal(rename, room.Settings.Name);
		Assert.Equal("hunter2", room.Settings.Password);
		Assert.True(room.Settings.IsPrivate);
		Assert.Equal(Basil.Domain.Mechanics.GameTeamType.TeamVs, room.Settings.MatchSettings.TeamType);
		Assert.Equal(Basil.Domain.Mechanics.GameWinCondition.Accuracy, room.Settings.MatchSettings.WinCondition);
	}

	[Fact]
	public async Task Rooms_ASizeOutOfRange_IsInvalid_AndAMissingRequiredOptionIsAUsageError()
	{
		var id = await OpenRoomAsync(CliFixture.Name("range"));

		Assert.Equal(2, (await fixture.RunAsync($"rooms {id} size --size 99")).Code);
		var (code, text) = await fixture.RunAsync($"rooms {id} move --user 3");
		Assert.Equal(2, code);
		Assert.Contains("--slot", text);
	}

	[Fact]
	public async Task Rooms_Mods_AreReadAsAcronyms()
	{
		var id = await OpenRoomAsync(CliFixture.Name("mods"));

		var (code, _) = await fixture.RunAsync($"rooms {id} mods --mods HD,DT");

		Assert.Equal(0, code);
		Assert.Equal(Basil.Domain.Mechanics.GameMods.Hidden | Basil.Domain.Mechanics.GameMods.DoubleTime, Lobby.Rooms[id].Settings.MatchSettings.Mods);
	}

	[Fact]
	public async Task Rooms_Refereeing_AddsAndRemovesAReferee()
	{
		var id = await OpenRoomAsync(CliFixture.Name("ref"));
		var name = CliFixture.Name("referee");
		await fixture.RunAsync($"users create --username {name} --password pw");
		var user = await fixture.UserAsync(name);

		var (added, _) = await fixture.RunAsync($"rooms {id} addref --user {user.Id}");
		var referees = Lobby.Rooms[id].Authority.Referees.Select(r => r.Id).ToList();
		var (removed, _) = await fixture.RunAsync($"rooms {id} removeref --user {user.Id}");

		Assert.Equal(0, added);
		Assert.Contains(user.Id, referees);
		Assert.Equal(0, removed);
		Assert.DoesNotContain(user.Id, Lobby.Rooms[id].Authority.Referees.Select(r => r.Id));
	}

	[Fact]
	public async Task Rooms_CommandOnAnUnknownUser_IsRefusedAsAConflict()
	{
		var id = await OpenRoomAsync(CliFixture.Name("ghost"));

		var (code, text) = await fixture.RunAsync($"rooms {id} kick --user 999999");

		Assert.Equal(1, code);
		Assert.Contains("notInRoom", text);
	}

	[Fact]
	public async Task Rooms_Close_RemovesTheRoom_AndAnUnknownRoomIsNotFound()
	{
		var id = await OpenRoomAsync(CliFixture.Name("close"));

		var (code, _) = await fixture.RunAsync($"rooms {id} close");
		var (again, text) = await fixture.RunAsync($"rooms {id} close");

		Assert.Equal(0, code);
		Assert.False(Lobby.Rooms.ContainsKey(id));
		Assert.Equal(1, again);
		Assert.Contains("notFound", text);
	}

	[Fact]
	public async Task Matches_ListAndShow_PrintTheMatchOfARoom()
	{
		var name = CliFixture.Name("match");
		var id = await OpenRoomAsync(name);
		var matchId = Lobby.Rooms[id].Match.Id;

		var (_, listed) = await fixture.RunAsync($"matches list --query ended:false");
		var (code, shown) = await fixture.RunAsync($"matches {matchId}");

		Assert.Contains(name, listed);
		Assert.Equal(0, code);
		Assert.Contains($"id:", shown);
		Assert.Contains("entries:", shown);
		Assert.Equal(1, (await fixture.RunAsync("matches 999999")).Code);
	}
}
