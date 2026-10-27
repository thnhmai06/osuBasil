using Basil.Application.Runtime.Connections;
using Basil.Application.Runtime.Multiplayer.Rooms;
using Basil.Application.Runtime.Users;
using Basil.Domain.Users;
using Basil.Testing;

namespace Basil.Bot.Tests.Support;

/// <summary>
///     A room opened with <c>!mp make</c> and the people around it: its creator, an online referee, a referee who is
///     offline, and a plain seated player.
/// </summary>
/// <param name="Room">The room.</param>
/// <param name="Creator">The creator, seated as host.</param>
/// <param name="Referee">A referee whose osu! client is online.</param>
/// <param name="OfflineReferee">A referee who has no open connection.</param>
/// <param name="Player">A seated player who holds no authority.</param>
internal sealed record StaffRoom(
	Room Room,
	BanchoConnection Creator,
	BanchoConnection Referee,
	UserRecord OfflineReferee,
	BanchoConnection Player)
{
	/// <summary>Opens a room and the people around it; their names start with the room's name.</summary>
	/// <param name="bot">The harness the room lives in.</param>
	/// <param name="name">The name of the room.</param>
	/// <returns>The room and its people.</returns>
	public static async Task<StaffRoom> OpenAsync(BotHarness bot, string name)
	{
		var creator = await RoomFixture.PlayerAsync(bot.App, name + "Creator");
		var room = await bot.MakeAsync(creator, name);
		var scope = room.ScopeTo(creator).Authority;

		var referee = await RoomFixture.PlayerAsync(bot.App, name + "Referee");
		Assert.True((await scope.AddRefereeAsync(referee.Record, TestContext.Current.CancellationToken)).IsOk);

		var offlineReferee = await RoomFixture.PlayerAsync(bot.App, name + "Away");
		Assert.True((await scope.AddRefereeAsync(offlineReferee.Record, TestContext.Current.CancellationToken)).IsOk);
		bot.App.Users.Close(offlineReferee, ConnectionCloseReason.TimedOut);
		Assert.DoesNotContain(offlineReferee.Record, bot.App.Users.Online.Keys);

		var player = await RoomFixture.PlayerAsync(bot.App, name + "Player");
		Assert.True((await bot.TypeAsync(player, "#osu", $"!mp join {room.Id}")).Succeeded);

		return new StaffRoom(room, creator, referee, offlineReferee.Record, player);
	}
}