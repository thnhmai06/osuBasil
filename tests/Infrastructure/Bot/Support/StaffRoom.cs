using Basil.Application.Multiplayer.Objects;
using Basil.Application.Multiplayer.Objects.Rooms;
using Basil.Application.Users.Objects;
using Basil.Application.Users.Objects.Connections;
using Basil.Domain.Authentication;
using Basil.Testing;

namespace Basil.Infrastructure.Bot.Tests.Support;

internal sealed record StaffRoom(
	Room Room,
	StableConnection Creator,
	StableConnection Referee,
	UserRecord OfflineReferee,
	StableConnection Player)
{
	public static async Task<StaffRoom> OpenAsync(BotHarness bot, string name)
	{
		var creator = await RoomFixture.PlayerAsync(bot.App, name + "Creator");
		var room = await bot.MakeAsync(creator, name);
		var scope = room.ScopeTo(creator).Authority;

		var referee = await RoomFixture.PlayerAsync(bot.App, name + "Referee");
		Assert.True(await scope.AddRefereeAsync(UserReference.ById(referee.Record.Id)) == RoomOutcome.Ok);

		var offlineReferee = await RoomFixture.PlayerAsync(bot.App, name + "Away");
		Assert.True(await scope.AddRefereeAsync(UserReference.ById(offlineReferee.Record.Id)) == RoomOutcome.Ok);
		bot.App.Users.Close(offlineReferee, SessionEndReason.TimedOut);
		Assert.DoesNotContain(offlineReferee.Record, bot.App.Users.Online.Keys);

		var player = await RoomFixture.PlayerAsync(bot.App, name + "Player");
		Assert.True((await bot.TypeAsync(player, "#osu", $"!mp join {room.Id}")).Succeeded);

		return new StaffRoom(room, creator, referee, offlineReferee.Record, player);
	}
}