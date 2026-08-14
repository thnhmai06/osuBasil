using System.Collections.Concurrent;
using System.Globalization;
using System.IO.Compression;
using Basil.LoadTests.Client;
using Basil.LoadTests.Configuration;
using Basil.LoadTests.Helpers;
using Basil.LoadTests.Models;
using Basil.Protocol.Multiplayer;
using Basil.Protocol.Packets;
using NBomber.Contracts;
using NBomber.CSharp;

namespace Basil.LoadTests.Scenarios;

/// <summary>
///     Multiplayer tournament-round workload. The scale axis is <b>rooms</b>, not players — the server
///     allocates match ids from a fixed 64-slot pool and serializes every mutation inside one room
///     behind <c>MatchSession.Lock</c>, so "more concurrency" here means more rooms, each capped at 16
///     players.
/// </summary>
/// <remarks>
///     <para>
///         Within a room, only the host's <c>MatchStart</c> actually starts play (a non-host's is
///         silently ignored), so the create→join→ready→start→play→complete sequence is coordinated via
///         a per-room <see cref="TaskCompletionSource{TResult}" /> that publishes the match id from the
///         host's virtual user to the room's other virtual users.
///     </para>
///     <para>
///         Rounds are paced with fixed delays rather than by parsing every peer's broadcast
///         ready/load-complete packet — that would need a full client-side match-state machine, which
///         is disproportionate for a harness whose job is generating realistic traffic and measuring
///         server resource usage, not asserting exact match semantics.
///     </para>
/// </remarks>
public sealed class MultiplayerScenario : IBasilScenario
{
	public string Id => "multiplayer";

	public IReadOnlyList<ScenarioProps> Build(BasilScenarioContext context)
	{
		var settings = context.GetScenarioSettings<MultiplayerSettings>(Id);
		if (!settings.Enabled) return [];

		var clientFactory = context.ClientFactory;
		var reportFolder = context.ReportFolder;

		(int Id, string Md5)? beatmap = null;
		if (!string.IsNullOrEmpty(settings.BeatmapsetFixture))
			try
			{
				beatmap = ResolveOrIngestBeatmapAsync(context).GetAwaiter().GetResult();
			}
			catch (Exception ex)
			{
				context.LogWarning($"Beatmap fixture ingestion failed ({ex.Message}); rooms will run with MapId = 0.");
			}

		var props = new List<ScenarioProps>();

		foreach (var roomCount in settings.Rooms)
		{
			if (roomCount > 64)
				throw new InvalidOperationException(
					$"'{Id}' room count {roomCount} exceeds the server's 64-match id pool.");
			if (settings.PlayersPerRoom > 16)
				throw new InvalidOperationException(
					$"'{Id}' players-per-room {settings.PlayersPerRoom} exceeds the server's 16-slot limit.");

			var totalPlayers = roomCount * settings.PlayersPerRoom;
			var accounts = context.Accounts.Take(totalPlayers).ToArray();
			if (accounts.Length < totalPlayers)
				throw new InvalidOperationException(
					$"'{Id}' at {roomCount} rooms x {settings.PlayersPerRoom} players needs {totalPlayers} " +
					$"accounts but only {accounts.Length} exist.");

			var playersPerRoom = settings.PlayersPerRoom;
			var roundsPerRoom = Math.Max(1, settings.RoundsPerRoom);
			var scoreUpdatesPerSecond = Math.Max(1, settings.ScoreUpdatesPerSecond);
			var roomMatchIds = new ConcurrentDictionary<int, TaskCompletionSource<int>>();
			var metrics = new MultiplayerMetrics();
			var roomCountCaptured = roomCount;
			var fixtureBeatmap = beatmap;

			var scenario = Scenario.Create($"{Id}_{roomCount}", async ctx =>
				{
					var instance = ctx.ScenarioInfo.InstanceNumber;
					var roomIndex = instance / playersPerRoom;
					var isHost = instance % playersPerRoom == 0;

					try
					{
						var account = accounts[instance];

						await using var client = new BanchoClient(clientFactory, account);
						var outcome = await client.LoginAsync(ctx.ScenarioCancellationToken);
						if (!outcome.Success)
							return Response.Fail(message: outcome.FailureReason ?? "unknown-failure");

						var roomReady = roomMatchIds.GetOrAdd(roomIndex,
							_ => new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously));

						int matchId;
						if (isHost)
						{
							var match = BuildEmptyMatch($"load-room-{roomIndex}", account.UserId ?? 0, fixtureBeatmap);
							client.Send(ClientPacketWriter.CreateMatch(match));
							var frames = await client.PollAsync(ctx.ScenarioCancellationToken);
							matchId = ExtractMatchId(frames) ?? -1;
							roomReady.TrySetResult(matchId);
							if (matchId < 0) return Response.Fail(statusCode: "create-match-failed");

							// CreateMatch's own map fields are ignored server-side (MatchMembershipService
							// .CreateAsync never applies them) — the beatmap only gets assigned via a
							// follow-up MatchChangeSettings, which re-resolves it by md5.
							if (fixtureBeatmap is not null)
							{
								client.Send(ClientPacketWriter.MatchChangeSettings(
									BuildEmptyMatch($"load-room-{roomIndex}", account.UserId ?? 0, fixtureBeatmap)));
								await client.PollAsync(ctx.ScenarioCancellationToken);
							}
						}
						else
						{
							using var joinTimeout =
								CancellationTokenSource.CreateLinkedTokenSource(ctx.ScenarioCancellationToken);
							joinTimeout.CancelAfter(TimeSpan.FromSeconds(30));
							matchId = await roomReady.Task.WaitAsync(joinTimeout.Token);
							if (matchId < 0) return Response.Fail(statusCode: "room-create-failed");

							client.Send(ClientPacketWriter.JoinMatch(matchId, ""));
							await client.PollAsync(ctx.ScenarioCancellationToken);
						}

						client.Send(ClientPacketWriter.MatchChangeMods(0));
						client.Send(ClientPacketWriter.MatchReady());
						await client.PollAsync(ctx.ScenarioCancellationToken);

						for (var round = 0; round < roundsPerRoom; round++)
						{
							if (isHost)
							{
								await Task.Delay(TimeSpan.FromSeconds(2), ctx.ScenarioCancellationToken);
								client.Send(ClientPacketWriter.MatchStart());
							}

							await client.PollAsync(ctx.ScenarioCancellationToken);

							client.Send(ClientPacketWriter.MatchLoadComplete());
							await client.PollAsync(ctx.ScenarioCancellationToken);

							var scoreInterval = TimeSpan.FromSeconds(1.0 / scoreUpdatesPerSecond);
							var playUntil = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(3);
							while (DateTimeOffset.UtcNow < playUntil)
							{
								client.Send(ClientPacketWriter.MatchScoreUpdate(BuildScoreFrame(account.UserId ?? 0)));
								var pollStart = DateTimeOffset.UtcNow;
								await client.PollAsync(ctx.ScenarioCancellationToken);
								metrics.RecordScoreUpdate((DateTimeOffset.UtcNow - pollStart).TotalMilliseconds);
								await Task.Delay(scoreInterval, ctx.ScenarioCancellationToken);
							}

							client.Send(ClientPacketWriter.MatchComplete());
							await client.PollAsync(ctx.ScenarioCancellationToken);
							metrics.RecordRoundCompleted();
						}

						client.Send(ClientPacketWriter.PartMatch());
						await client.LogoutAsync(ctx.ScenarioCancellationToken);
						return Response.Ok(statusCode: "completed");
					}
					catch (Exception ex) when (ex is not OperationCanceledException ||
					                           !ctx.ScenarioCancellationToken.IsCancellationRequested)
					{
						// A failed host must not leave its room's followers waiting the full 30s
						// join timeout for nothing.
						if (isHost)
							roomMatchIds
								.GetOrAdd(roomIndex,
									_ => new TaskCompletionSource<int>(TaskCreationOptions
										.RunContinuationsAsynchronously))
								.TrySetResult(-1);

						return Response.Fail(statusCode: ex.GetType().Name, message: ex.Message);
					}
				})
				.WithLoadSimulations(Simulation.KeepConstant(totalPlayers, settings.Duration))
				.WithoutWarmUp()
				.WithMaxFailCount(1_000_000)
				.WithClean(_ =>
				{
					metrics.WriteReport(reportFolder, roomCountCaptured);
					return Task.CompletedTask;
				});

			props.Add(scenario);
		}

		return props;
	}

	private static MatchPacket BuildEmptyMatch(string name, int hostId, (int Id, string Md5)? beatmap)
	{
		var slots = Enumerable.Range(0, 16)
			.Select(_ => new MatchSlotPacket(0, 0, 0, null))
			.ToArray();

		return new MatchPacket(
			0,
			false,
			0,
			name,
			"",
			beatmap is null ? "" : "Load Test Beatmap",
			beatmap?.Id ?? 0,
			beatmap?.Md5 ?? "",
			slots,
			hostId,
			0,
			0,
			0,
			false,
			0);
	}

	private static ScoreFrame BuildScoreFrame(int playerId)
	{
		return new ScoreFrame(
			Environment.TickCount,
			playerId,
			10, 1, 0, 5, 0, 0,
			100_000, 50, 50,
			true, 100, 0, false);
	}

	private static int? ExtractMatchId(IEnumerable<ServerPacketFrame> frames)
	{
		foreach (var frame in frames)
		{
			if (frame.Type is not (ServerPackets.MatchJoinSuccess or ServerPackets.NewMatch)) continue;
			var match = new PacketReader(frame.Payload).ReadMatch();
			return match.Id;
		}

		return null;
	}

	/// <summary>
	///     Zips the repo's own protocol-test fixture <c>.osu</c> into an in-memory <c>.osz</c> and
	///     ingests it via the admin API (bypass mode is assumed — this harness never sets an admin key),
	///     so multiplayer rooms have a real beatmap to assign instead of always running with
	///     <c>MapId = 0</c>.
	/// </summary>
	private static async Task<(int Id, string Md5)?> ResolveOrIngestBeatmapAsync(BasilScenarioContext context)
	{
		var osuFixturePath = RepoPaths.Resolve("tests/Basil.Infrastructure.Tests/Fixtures/vivid_with_setid.osu");
		if (!File.Exists(osuFixturePath)) return null;

		using var zipStream = new MemoryStream();
		await using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, true))
		{
			var entry = archive.CreateEntry(Path.GetFileName(osuFixturePath));
			await using var entryStream = await entry.OpenAsync();
			await using var fileStream = File.OpenRead(osuFixturePath);
			await fileStream.CopyToAsync(entryStream);
		}

		var apiClient = new BasilApiClient(context.ClientFactory);
		await apiClient.UploadBeatmapsetAsync(zipStream.ToArray(), "vivid.osz", "");

		// ponytail: the mapset list read can briefly lag the reconcile write; a few short
		// retries beat hard-coding a fixed pre-delay for every profile.
		for (var attempt = 0; attempt < 5; attempt++)
		{
			if (await apiClient.ResolveSampleBeatmapsetIdAsync() is { } mapsetId)
				return await apiClient.ResolveFirstBeatmapAsync(mapsetId);
			await Task.Delay(TimeSpan.FromMilliseconds(300));
		}

		return null;
	}

	/// <summary>Per-room-count counters, since NBomber has no built-in "rounds completed" or per-packet-type latency metric.</summary>
	private sealed class MultiplayerMetrics
	{
		private readonly Lock _lock = new();
		private readonly List<double> _scoreUpdateLatenciesMs = [];
		private long _roundsCompleted;
		private long _scoreUpdateCount;

		public void RecordRoundCompleted()
		{
			Interlocked.Increment(ref _roundsCompleted);
		}

		public void RecordScoreUpdate(double latencyMs)
		{
			Interlocked.Increment(ref _scoreUpdateCount);
			lock (_lock)
			{
				_scoreUpdateLatenciesMs.Add(latencyMs);
			}
		}

		public void WriteReport(string reportFolder, int roomCount)
		{
			double[] latencies;
			lock (_lock)
			{
				latencies = [.. _scoreUpdateLatenciesMs];
			}

			Array.Sort(latencies);
			var csvPath = Path.Combine(reportFolder, $"multiplayer-ops-{roomCount}.csv");
			using (var writer = new StreamWriter(csvPath))
			{
				writer.WriteLine("ScoreUpdateLatencyMs");
				foreach (var latency in latencies) writer.WriteLine(latency.ToString(CultureInfo.InvariantCulture));
			}

			var summaryPath = Path.Combine(reportFolder, $"multiplayer-summary-{roomCount}.md");
			var lines = new List<string>
			{
				$"# Multiplayer summary — {roomCount} room(s)",
				"",
				$"- Rounds completed: {Interlocked.Read(ref _roundsCompleted)}",
				$"- Score updates sent: {Interlocked.Read(ref _scoreUpdateCount)}"
			};

			if (latencies.Length > 0)
			{
				lines.Add($"- Score-update round-trip p50: {Percentile(latencies, 0.50):F1} ms");
				lines.Add($"- Score-update round-trip p95: {Percentile(latencies, 0.95):F1} ms");
				lines.Add($"- Score-update round-trip p99: {Percentile(latencies, 0.99):F1} ms");
			}

			File.WriteAllLines(summaryPath, lines);
		}

		private static double Percentile(double[] sortedValues, double percentile)
		{
			if (sortedValues.Length == 0) return 0;
			var index = (int)Math.Ceiling(percentile * sortedValues.Length) - 1;
			return sortedValues[Math.Clamp(index, 0, sortedValues.Length - 1)];
		}
	}
}