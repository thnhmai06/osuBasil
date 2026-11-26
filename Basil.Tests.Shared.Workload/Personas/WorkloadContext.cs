using System.Drawing;
using System.Runtime.CompilerServices;
using Basil.Domain.Scores;
using Basil.Domain.Spectating;
using Basil.Protocol.Game.Stable.Models.Users;
using Basil.Tests.Shared.Clients;
using Basil.Tests.Shared.Clients.OsuWeb;
using Basil.Tests.Shared.Clients.Rest;
using Basil.Tests.Shared.Clients.Stable;
using Basil.Tests.Shared.Workload.Outcomes;
using Basil.Tests.Shared.Workload.World;
using NBomber.Contracts;

namespace Basil.Tests.Shared.Workload.Personas;

/// <summary>Shared collaborators and knobs passed to every persona.</summary>
public sealed class WorkloadContext(
	Endpoint endpoint,
	int ircPort,
	HttpClient http,
	WorldData worldData,
	AccountPool accounts,
	WorldState world,
	OutcomeTally outcomes,
	LoginLockoutGuard lockoutGuard,
	TournamentClock tournamentClock,
	int seed,
	CancellationToken abortToken)
{
	private static readonly TimeSpan SignOutTimeout = TimeSpan.FromSeconds(5);

	private readonly IReadOnlyList<BeatmapEntry> _beatmaps = worldData.AllBeatmaps;
	private readonly ConditionalWeakTable<StableClient, object?> _dropped = new();

	/// <summary>Gets the server endpoint.</summary>
	public Endpoint Endpoint => endpoint;

	/// <summary>Gets the IRC TLS port.</summary>
	public int IrcPort => ircPort;

	/// <summary>Gets the shared HTTP client.</summary>
	public HttpClient Http => http;

	/// <summary>Gets the seeded beatmaps, seasonals and FAQs.</summary>
	public WorldData WorldData => worldData;

	/// <summary>Gets the account pool.</summary>
	public AccountPool Accounts => accounts;

	/// <summary>Gets the live shared world state.</summary>
	public WorldState World => world;

	/// <summary>Gets the outcome tally.</summary>
	public OutcomeTally Outcomes => outcomes;

	/// <summary>Gets the login lockout guard.</summary>
	public LoginLockoutGuard LockoutGuard => lockoutGuard;

	/// <summary>Gets the shared tournament clock used by Spike runs.</summary>
	public TournamentClock TournamentClock => tournamentClock;

	/// <summary>Gets the fixed random seed for this run.</summary>
	public int Seed => seed;

	/// <summary>Gets a token cancelled when the lockout guard trips.</summary>
	public CancellationToken AbortToken => abortToken;

	/// <summary>Gets or sets the poll interval of simulated osu! clients.</summary>
	public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(1);

	/// <summary>Gets or sets the simulated play duration of one round.</summary>
	public TimeSpan RoundDuration { get; init; } = TimeSpan.FromSeconds(12);

	/// <summary>Gets or sets the timeout for waiting on server responses.</summary>
	public TimeSpan WaitTimeout { get; init; } = TimeSpan.FromSeconds(15);

	/// <summary>Creates a token linked to both NBomber's scenario cancellation and the run abort guard.</summary>
	public CancellationTokenSource LinkToken(IScenarioContext context)
	{
		return CancellationTokenSource.CreateLinkedTokenSource(context.ScenarioCancellationToken, abortToken);
	}

	/// <summary>Picks a beatmap deterministically using <paramref name="random"/>.</summary>
	public BeatmapEntry PickBeatmap(Random random)
	{
		if (_beatmaps.Count == 0)
			throw new InvalidOperationException("World data contains no beatmaps.");
		return _beatmaps[random.Next(_beatmaps.Count)];
	}

	/// <summary>Picks a beatmapset deterministically using <paramref name="random"/>.</summary>
	public BeatmapsetEntry PickBeatmapset(Random random)
	{
		if (worldData.Beatmapsets.Count == 0)
			throw new InvalidOperationException("World data contains no beatmapsets.");
		return worldData.Beatmapsets[random.Next(worldData.Beatmapsets.Count)];
	}

	/// <summary>Signs an osu! client in inside a <c>login</c> step; an unexpected refusal feeds the lockout guard and quarantines the leased account.</summary>
	public async Task<StableLoginResult?> LoginStableAsync(StableClient client, IScenarioContext context,
		CancellationToken ct, bool refusalExpected = false, AccountLease? lease = null)
	{
		var (outcome, result) = await StepExecution.RunAsync(Channels.Login, context, outcomes, async () =>
		{
			var reply = await client.LoginAsync(ct);
			var classified = OutcomeClassifier.FromStableLogin(reply, refusalExpected);
			if (!refusalExpected && !reply.Accepted && (int)reply.Status is >= 200 and < 300)
			{
				lease?.Quarantine();
				lockoutGuard.RecordRefusedLogin(client.Identity.Name, context);
			}

			return (classified, reply);
		}, ct);

		if (result is { Accepted: true })
		{
			_dropped.Remove(client);
			world.PutOnline(new OnlineUser(result.UserId, client.Identity.Name, false, string.Empty));
		}

		return outcome.Outcome == Outcome.Ok || refusalExpected ? result : null;
	}

	/// <summary>Polls an osu! client inside a <c>poll</c> step; a client the server has dropped is marked and is not polled again.</summary>
	public async Task<StableExchange?> PollStableAsync(StableClient client, IScenarioContext context, CancellationToken ct)
	{
		if (IsDropped(client)) return null;

		var (_, exchange) = await StepExecution.RunAsync(Channels.Poll, context, outcomes, async () =>
		{
			var reply = await client.PollAsync(ct);
			return (OutcomeClassifier.FromStableExchange(reply), reply);
		}, ct);
		if (exchange is not null && OutcomeClassifier.IsDropped(exchange)) _dropped.TryAdd(client, null);
		return exchange;
	}

	/// <summary>Gets whether the server dropped the session of an osu! client, so it has to sign in again.</summary>
	public bool IsDropped(StableClient client)
	{
		return _dropped.TryGetValue(client, out _);
	}

	/// <summary>Polls an osu! client until a matching packet arrives, measuring each poll as a <c>poll</c> step.</summary>
	public async Task<T?> PollUntilAsync<T>(StableClient client, Func<T, bool>? match, TimeSpan timeout,
		IScenarioContext context, CancellationToken ct) where T : StablePacket
	{
		var deadline = DateTimeOffset.UtcNow + timeout;
		while (!ct.IsCancellationRequested)
		{
			var exchange = await PollStableAsync(client, context, ct);
			if (exchange is null || !exchange.IsSuccess || IsDropped(client)) return null;

			var found = exchange.Packets.OfType<T>().FirstOrDefault(packet => match is null || match(packet));
			if (found is not null) return found;
			if (DateTimeOffset.UtcNow >= deadline) return null;

			await SafeDelayAsync(PollInterval, ct);
		}

		return null;
	}

	/// <summary>Signs an osu! client out inside a <c>logout</c> step and removes it from <see cref="World"/>; the sign-out is sent even when the run is being cancelled.</summary>
	public async Task LogoutStableAsync(StableClient client, IScenarioContext context)
	{
		if (client.Token is null) return;

		var userId = client.UserId;
		try
		{
			using var timeout = new CancellationTokenSource(SignOutTimeout);
			await StepExecution.RunAsync(Channels.Logout, context, outcomes, async () =>
			{
				var reply = await client.LogoutAsync(timeout.Token);
				return OutcomeClassifier.FromHttpStatus(reply.Status);
			}, CancellationToken.None);
		}
		finally
		{
			if (userId > 0) world.RemoveOnline(userId);
		}
	}

	/// <summary>Signs a REST client out inside a <c>logout</c> step; the sign-out is sent even when the run is being cancelled.</summary>
	public async Task LogoutRestAsync(RestClient rest, IScenarioContext context)
	{
		if (rest.Token is null) return;

		using var timeout = new CancellationTokenSource(SignOutTimeout);
		await StepExecution.RunAsync(Channels.Logout, context, outcomes, async () =>
		{
			var reply = await rest.SignOutAsync(timeout.Token);
			return OutcomeClassifier.FromRest(reply);
		}, CancellationToken.None);
	}

	/// <summary>Signs an osu! client out without measuring it, for shutdown; never throws.</summary>
	public async Task SignOutQuietAsync(StableClient client)
	{
		if (client.Token is null) return;

		var userId = client.UserId;
		try
		{
			using var timeout = new CancellationTokenSource(SignOutTimeout);
			await client.LogoutAsync(timeout.Token);
		}
		catch (Exception)
		{
			// Best effort: the server ends a silent session by itself.
		}
		finally
		{
			if (userId > 0) world.RemoveOnline(userId);
		}
	}

	/// <summary>Signs a REST client out without measuring it, for shutdown; never throws.</summary>
	public static async Task SignOutQuietAsync(RestClient rest)
	{
		if (rest.Token is null) return;

		try
		{
			using var timeout = new CancellationTokenSource(SignOutTimeout);
			await rest.SignOutAsync(timeout.Token);
		}
		catch (Exception)
		{
			// Best effort: the server ends a silent session by itself.
		}
	}

	/// <summary>Builds a valid <see cref="ChangeAction"/> packet for idle or playing.</summary>
	public static ChangeAction PlayingAction(BeatmapEntry map, int mods = 0)
	{
		return new ChangeAction(2, $"{map.Version}", map.Md5, (uint)mods, (byte)map.Mode, map.Id);
	}

	/// <summary>Builds an idle <see cref="ChangeAction"/> packet.</summary>
	public static ChangeAction IdleAction()
	{
		return new ChangeAction(0, string.Empty, string.Empty, 0, 0, 0);
	}

	/// <summary>Builds a realistic mid-play <see cref="ScoreFrame"/>.</summary>
	public static ScoreFrame BuildScoreFrame(int tick, int slotId, Random random)
	{
		var n300 = (tick + 1) * 12;
		var n100 = tick % 3;
		var n50 = 0;
		var miss = tick > 0 && tick % 7 == 0 ? 1 : 0;
		var combo = (ushort)Math.Min(ushort.MaxValue, n300 + n100);
		var score = (n300 * 300 + n100 * 100) * (1 + combo / 25);
		var state = new ScoreState(new HitCounts(n300, n100, n50, n300 / 4, n100, miss), score, combo, combo, miss == 0);
		return new ScoreFrame(TimeSpan.FromSeconds(tick + 1), slotId, state, 200, 0, null);
	}

	/// <summary>Builds a <see cref="SpectatorBundle"/> carrying cursor frames and a score frame.</summary>
	public static SpectatorBundle BuildSpectatorBundle(int tick, int slotId, Random random, SpectatorAction action)
	{
		var frames = new List<ReplayFrame>(8);
		for (var i = 0; i < 8; i++)
		{
			var time = TimeSpan.FromMilliseconds((tick * 1000) + (i * 120));
			var point = new PointF(random.Next(32, 480), random.Next(32, 352));
			var keys = (i & 1) == 0 ? KeyState.Left2 : KeyState.Right2;
			frames.Add(new ReplayFrame(keys, TaikoDrumState.None, point, time));
		}

		return new SpectatorBundle(action, frames, BuildScoreFrame(tick, slotId, random), tick);
	}

	/// <summary>Delays without throwing when <paramref name="ct"/> is cancelled.</summary>
	public static async Task SafeDelayAsync(TimeSpan delay, CancellationToken ct)
	{
		if (delay <= TimeSpan.Zero || ct.IsCancellationRequested) return;
		try
		{
			await Task.Delay(delay, ct);
		}
		catch (OperationCanceledException)
		{
			// Cooperative cancellation.
		}
	}

	/// <summary>Maps an integer game mode to the REST API camelCase name.</summary>
	public static string RestModeName(int mode)
	{
		return mode switch
		{
			1 => "taiko",
			2 => "catch",
			3 => "mania",
			_ => "standard"
		};
	}
}
