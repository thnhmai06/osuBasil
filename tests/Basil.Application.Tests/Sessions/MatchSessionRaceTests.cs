using Basil.Application.Sessions.Multiplayer;
using Basil.Domain.Beatmaps;
using Basil.Domain.Multiplayer;
using Basil.Domain.Scores;

namespace Basil.Application.Tests.Sessions;

/// <summary>
///     Proves the slot-assignment race is real under concurrent access — two threads can read the
///     same free slot index and lose one player's occupancy — and that holding
///     <see cref="MatchSession.Lock" /> across the read-mutate sequence closes it.
/// </summary>
public class MatchSessionRaceTests
{
	private static MatchSession MakeMatch()
	{
		return new MatchSession(
			0, "race test", "",
			"", 0, new string('a', 32), 1,
			GameMode.Standard, Mods.NoMod, MatchWinCondition.Score,
			MatchTeamType.HeadToHead, false, 0, "#mp_0");
	}

	/// <summary>
	///     Reproduces the slot-assignment hazard under concurrent access: two threads read the same
	///     free slot index before either writes, so one userSession's occupancy is silently lost.
	///     A `Task.Delay` between the read and the write widens the window so the interleaving is
	///     reliably observed rather than being timing-dependent.
	/// </summary>
	[Fact]
	public async Task UnsynchronizedFreeSlotLookup_CanLoseAPlayerToADoubleAssignment()
	{
		var match = MakeMatch();

		var first = OccupyFreeSlotWithoutLockAsync(match, 1);
		var second = OccupyFreeSlotWithoutLockAsync(match, 2);
		await Task.WhenAll(first, second);

		var occupiedPlayerIds = match.Slots.Where(s => !s.Empty).Select(s => s.PlayerId).ToList();
		Assert.Single(occupiedPlayerIds); // one write clobbered the other — exactly the bug the lock fixes
	}

	private static async Task OccupyFreeSlotWithoutLockAsync(MatchSession match, int playerId)
	{
		var slotId = match.GetFreeSlotId();
		await Task.Delay(20); // widen the TOCTOU window between read and write
		match.Slots[slotId!.Value].PlayerId = playerId;
		match.Slots[slotId.Value].Status = SlotStatus.NotReady;
	}

	[Fact]
	public async Task ConcurrentJoins_UnderLock_NeverDoubleOccupyASlotAndFillsExactlyN()
	{
		var match = MakeMatch();
		var tasks = Enumerable.Range(1, 16).Select(playerId => JoinUnderLockAsync(match, playerId));
		await Task.WhenAll(tasks);

		var occupied = match.Slots.Where(s => !s.Empty).ToList();
		Assert.Equal(16, occupied.Count);
		Assert.Equal(16, occupied.Select(s => s.PlayerId).Distinct().Count());
	}

	[Fact]
	public async Task ConcurrentJoins_UnderLock_RejectsJoinsOnceMatchIsFull()
	{
		var match = MakeMatch();
		var tasks = Enumerable.Range(1, 20).Select(playerId => JoinUnderLockAsync(match, playerId));
		var results = await Task.WhenAll(tasks);

		Assert.Equal(16, results.Count(joined => joined));
		Assert.Equal(4, results.Count(joined => !joined));
	}

	private static async Task<bool> JoinUnderLockAsync(MatchSession match, int playerId)
	{
		await match.Lock.WaitAsync();
		try
		{
			var slotId = match.GetFreeSlotId();
			if (slotId is null) return false;

			await Task.Delay(1); // still widen the window, but now inside the critical section
			match.Slots[slotId.Value].PlayerId = playerId;
			match.Slots[slotId.Value].Status = SlotStatus.NotReady;
			return true;
		}
		finally
		{
			match.Lock.Release();
		}
	}
}