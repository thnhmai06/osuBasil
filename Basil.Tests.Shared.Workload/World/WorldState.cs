using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using NBomber.Contracts;

namespace Basil.Tests.Shared.Workload.World;

/// <summary>A live multiplayer room tracked by the shared world.</summary>
public sealed record LiveRoom(
	int RoomId,
	string Name,
	string Password,
	int BeatmapId,
	string BeatmapMd5,
	bool IsTournament,
	bool InProgress,
	IReadOnlyList<int> PlayerIds);

/// <summary>A live online user tracked by the shared world.</summary>
public sealed record OnlineUser(int UserId, string Name, bool IsPlaying, string BeatmapMd5);

/// <summary>
///     Shared live state of the simulated world so every persona targets real rooms, playing users, online accounts
///     and scores.
/// </summary>
public sealed class WorldState
{
	private const int MaxRecentItems = 512;
	private readonly ConcurrentDictionary<int, OnlineUser> _online = new();

	private readonly ConcurrentDictionary<int, LiveRoom> _rooms = new();
	private readonly ConcurrentQueue<long> _scoreIds = new();
	private readonly ConcurrentQueue<string> _screenshots = new();

	/// <summary>Gets the shared live rooms by room id.</summary>
	public IReadOnlyDictionary<int, LiveRoom> Rooms => _rooms;

	/// <summary>Gets the shared online users by user id.</summary>
	public IReadOnlyDictionary<int, OnlineUser> OnlineUsers => _online;

	/// <summary>Registers or updates a live room.</summary>
	public void PutRoom(LiveRoom room)
	{
		_rooms[room.RoomId] = room;
	}

	/// <summary>Removes a closed room.</summary>
	public void RemoveRoom(int roomId)
	{
		_rooms.TryRemove(roomId, out _);
	}

	/// <summary>Registers or updates an online user.</summary>
	public void PutOnline(OnlineUser user)
	{
		_online[user.UserId] = user;
	}

	/// <summary>Removes a user who signed out.</summary>
	public void RemoveOnline(int userId)
	{
		_online.TryRemove(userId, out _);
	}

	/// <summary>Records a real score id observed from a leaderboard so replay reads target real scores.</summary>
	public void RecordScoreId(long scoreId)
	{
		if (scoreId <= 0) return;
		_scoreIds.Enqueue(scoreId);
		while (_scoreIds.Count > MaxRecentItems) _scoreIds.TryDequeue(out _);
	}

	/// <summary>Records a real uploaded screenshot filename.</summary>
	public void RecordScreenshot(string fileName)
	{
		if (string.IsNullOrWhiteSpace(fileName)) return;
		_screenshots.Enqueue(fileName);
		while (_screenshots.Count > MaxRecentItems) _screenshots.TryDequeue(out _);
	}

	/// <summary>Picks a live room, optionally filtering to tournament rooms.</summary>
	public LiveRoom? PickRoom(Random random, bool? tournamentOnly = null)
	{
		var snapshot = _rooms.Values
			.Where(room => tournamentOnly is null || room.IsTournament == tournamentOnly.Value)
			.ToArray();
		return snapshot.Length == 0 ? null : snapshot[random.Next(snapshot.Length)];
	}

	/// <summary>Picks an online user who is currently playing, excluding <paramref name="excludeUserId" />.</summary>
	public OnlineUser? PickPlayingUser(Random random, int excludeUserId = 0)
	{
		var snapshot = _online.Values
			.Where(user => user.IsPlaying && user.UserId != excludeUserId)
			.ToArray();
		return snapshot.Length == 0 ? null : snapshot[random.Next(snapshot.Length)];
	}

	/// <summary>Picks any online user, excluding <paramref name="excludeUserId" />.</summary>
	public OnlineUser? PickOnlineUser(Random random, int excludeUserId = 0)
	{
		var snapshot = _online.Values
			.Where(user => user.UserId != excludeUserId)
			.ToArray();
		return snapshot.Length == 0 ? null : snapshot[random.Next(snapshot.Length)];
	}

	/// <summary>Picks up to <paramref name="maxCount" /> online user ids.</summary>
	public IReadOnlyList<int> PickOnlineUserIds(Random random, int maxCount)
	{
		var snapshot = _online.Keys.ToArray();
		if (snapshot.Length == 0) return [];
		if (snapshot.Length <= maxCount) return snapshot;

		var result = new HashSet<int>(maxCount);
		for (var i = 0; i < maxCount * 2 && result.Count < maxCount; i++)
			result.Add(snapshot[random.Next(snapshot.Length)]);
		return [.. result];
	}

	/// <summary>Picks a known score id, or 0 when none has been recorded yet.</summary>
	public long PickScoreId(Random random)
	{
		var snapshot = _scoreIds.ToArray();
		return snapshot.Length == 0 ? 0 : snapshot[random.Next(snapshot.Length)];
	}

	/// <summary>Picks a known uploaded screenshot filename, or <see langword="null" /> when none has been uploaded yet.</summary>
	public string? PickScreenshot(Random random)
	{
		var snapshot = _screenshots.ToArray();
		return snapshot.Length == 0 ? null : snapshot[random.Next(snapshot.Length)];
	}
}

/// <summary>
///     Shared tournament clock for Spike tests: every room's referee waits for the same UTC instant before starting
///     its round.
/// </summary>
public sealed class TournamentClock
{
	private long _nextWaveTicks;

	/// <summary>
	///     Creates a clock that fires at <paramref name="firstWaveAt" /> and repeats every
	///     <paramref name="waveInterval" /> when set.
	/// </summary>
	public TournamentClock(DateTimeOffset? firstWaveAt = null, TimeSpan? waveInterval = null)
	{
		WaveInterval = waveInterval;
		_nextWaveTicks = firstWaveAt?.UtcTicks ?? 0;
	}

	/// <summary>Gets the interval between synchronized waves, or <see langword="null" /> for one-shot or disabled clock.</summary>
	public TimeSpan? WaveInterval { get; }

	/// <summary>Gets whether synchronized wave starts are active.</summary>
	public bool IsEnabled => Volatile.Read(ref _nextWaveTicks) > 0;

	/// <summary>Waits until the next scheduled wave instant, if enabled.</summary>
	public async Task WaitForNextWaveAsync(CancellationToken ct)
	{
		var targetTicks = Volatile.Read(ref _nextWaveTicks);
		if (targetTicks == 0) return;

		var now = DateTimeOffset.UtcNow;
		var target = new DateTimeOffset(targetTicks, TimeSpan.Zero);
		if (target <= now && WaveInterval is { } interval && interval > TimeSpan.Zero)
		{
			var elapsed = now - target;
			var steps = (long)(elapsed / interval) + 1;
			target = target.AddTicks(steps * interval.Ticks);
			Interlocked.CompareExchange(ref _nextWaveTicks, target.UtcTicks, targetTicks);
		}

		var delay = target - now;
		if (delay > TimeSpan.Zero) await Task.Delay(delay, ct);
	}
}

/// <summary>
///     Stops the run when many distinct accounts are refused at sign-in within a short window, which means the
///     account list no longer matches the server.
/// </summary>
/// <remarks>
///     The server keeps a failed attempt only for wrong credentials, per address and account, so one account refused again
///     and again
///     (for example <c>AlreadyOnline</c> after a slow sign-out) is not a lockout risk and does not count towards the
///     burst.
/// </remarks>
public sealed class LoginLockoutGuard(
	CancellationTokenSource runAbort,
	int burstThreshold = 10,
	TimeSpan? window = null)
{
	private readonly ConcurrentDictionary<string, DateTimeOffset> _lastRefusal = new(StringComparer.OrdinalIgnoreCase);
	private readonly TimeSpan _window = window ?? TimeSpan.FromSeconds(60);
	private int _tripped;

	/// <summary>Gets whether the lockout guard has tripped and cancelled the run.</summary>
	public bool IsTripped => Volatile.Read(ref _tripped) != 0;

	/// <summary>
	///     Records an unexpected refused sign-in of <paramref name="account" />; stops the run when enough distinct
	///     accounts were refused within the window.
	/// </summary>
	/// <param name="account">The refused account name.</param>
	/// <param name="context">The scenario context used to stop NBomber as well as cancel the run token.</param>
	public void RecordRefusedLogin(string account, IScenarioContext? context = null)
	{
		var now = DateTimeOffset.UtcNow;
		_lastRefusal[account] = now;
		var cutoff = now - _window;
		var distinct = 0;
		foreach (var (name, at) in _lastRefusal)
			if (at < cutoff) _lastRefusal.TryRemove(name, out _);
			else distinct++;

		if (distinct < burstThreshold || Interlocked.Exchange(ref _tripped, 1) != 0) return;

		// Stop first: cancelling runs the waiting continuations on this thread and may not come back soon.
		context?.StopCurrentTest(
			$"{distinct} distinct accounts were refused at sign-in within {_window.TotalSeconds:0} s; accounts.csv does not match the server.");
		runAbort.Cancel();
	}
}

/// <summary>Creates deterministic <see cref="Random" /> instances from the fixed run seed.</summary>
public static class SeededRandom
{
	/// <summary>Creates a generator that is the same in every process for the same run seed, scenario, copy and invocation.</summary>
	public static Random For(int runSeed, string scenario, int instanceNumber, long invocationNumber)
	{
		var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{runSeed}:{scenario}:{instanceNumber}:{invocationNumber}"));
		return new Random(BitConverter.ToInt32(hash, 0));
	}
}