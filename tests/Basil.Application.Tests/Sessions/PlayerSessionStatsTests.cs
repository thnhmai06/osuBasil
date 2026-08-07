using Basil.Application.Sessions;
using Basil.Domain.Beatmaps;
using Basil.Domain.Users;

namespace Basil.Application.Tests.Sessions;

/// <summary>
///     Verifies `GameSession.CurrentStats` indexes per-mode cached player stats by the session's
///     current status mode, returning null for a mode with no cached entry.
/// </summary>
public class PlayerSessionStatsTests
{
	[Fact]
	public void CurrentStats_IndexesByCurrentStatusMode()
	{
		var session = new GameSession(1, "cmyui", "token", UserPrivileges.Unrestricted, DateTimeOffset.UnixEpoch)
		{
			ModeStats =
			{
				[GameMode.Standard] = new CachedPlayerStats(100, 90, 10, 5),
				[GameMode.Taiko] = new CachedPlayerStats(200, 180, 20, 12)
			}
		};

		Assert.Equal(5, session.CurrentStats!.Rank);

		session.Status.Mode = GameMode.Taiko;
		Assert.Equal(12, session.CurrentStats!.Rank);
	}

	[Fact]
	public void CurrentStats_NoEntryForMode_ReturnsNull()
	{
		var session = new GameSession(1, "cmyui", "token", UserPrivileges.Unrestricted, DateTimeOffset.UnixEpoch);

		Assert.Null(session.CurrentStats);
	}
}