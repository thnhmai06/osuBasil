using Basil.LoadTests.Client;
using Basil.LoadTests.Configuration;
using Basil.LoadTests.Models;
using Basil.Protocol.Multiplayer;
using NBomber.Contracts;
using NBomber.CSharp;

namespace Basil.LoadTests.Scenarios;

/// <summary>
///     Holds N concurrent subscribers open on a <c>.../live</c> Server-Sent Events endpoint for the
///     scenario's duration. Unlike ordinary GETs, these connections are long-lived and pin server
///     resources for as long as they're held — the honest place to watch handle/thread growth vs.
///     subscriber count (read from the shared resource timeline, not this scenario).
/// </summary>
/// <remarks>
///     A <c>.../live</c> stream never flushes anything — not even response headers — until it has a
///     snapshot to send (<c>LiveSseRoutes.SubscribeWithSnapshot</c> only yields once
///     <c>readLatestSnapshot()</c> returns non-null); an idle target with no snapshot published yet
///     hangs a subscriber forever. A user's gameplay stream only gets a snapshot once they actually
///     play, so this scenario creates one throwaway multiplayer match up front (whose settings
///     snapshot exists the instant it's created) and every subscriber follows that match's
///     <c>/settings/live</c> stream instead of an arbitrary, likely-idle user.
/// </remarks>
public sealed class SseScenario : IBasilScenario
{
	public string Id => "sse";

	public IReadOnlyList<ScenarioProps> Build(BasilScenarioContext context)
	{
		var settings = context.GetScenarioSettings<SseSettings>(Id);
		if (!settings.Enabled) return [];

		var clientFactory = context.ClientFactory;
		var reportFolder = context.ReportFolder;

		var anchorAccount = context.Accounts[^1];
		var anchorClient = new BanchoClient(clientFactory, anchorAccount);
		var anchorMatchId = CreateAnchorMatchAsync(anchorClient, anchorAccount, new BasilApiClient(clientFactory))
			.GetAwaiter().GetResult();
		if (anchorMatchId is null)
		{
			context.LogWarning($"'{Id}' could not create an anchor match; skipping.");
			return [];
		}

		var props = new List<ScenarioProps>();

		foreach (var n in settings.ConcurrentUsers)
		{
			var metrics = new SseMetrics();
			var n1 = n;

			var scenario = Scenario.Create($"{Id}_{n}", async ctx =>
				{
					var eventCount = 0;
					try
					{
						using var client = clientFactory.CreateClient();
						client.Timeout = Timeout.InfiniteTimeSpan;

						var connectStart = DateTimeOffset.UtcNow;
						using var response = await client.GetAsync(
							clientFactory.BuildUri("api", $"/matches/{anchorMatchId}/settings/live"),
							HttpCompletionOption.ResponseHeadersRead, ctx.ScenarioCancellationToken);

						if (!response.IsSuccessStatusCode)
							return Response.Fail(statusCode: ((int)response.StatusCode).ToString());

						metrics.RecordConnected();
						await using var stream =
							await response.Content.ReadAsStreamAsync(ctx.ScenarioCancellationToken);
						using var reader = new StreamReader(stream);

						var firstEvent = true;
						var lastEventAt = connectStart;

						using var deadline =
							CancellationTokenSource.CreateLinkedTokenSource(ctx.ScenarioCancellationToken);
						deadline.CancelAfter(settings.Duration);

						try
						{
							while (!deadline.IsCancellationRequested)
							{
								var line = await reader.ReadLineAsync(deadline.Token);
								if (line is null) break;
								if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;

								var now = DateTimeOffset.UtcNow;
								if (firstEvent)
								{
									metrics.RecordTimeToFirstEvent((now - connectStart).TotalMilliseconds);
									firstEvent = false;
								}
								else
								{
									metrics.RecordInterEventGap((now - lastEventAt).TotalMilliseconds);
								}

								lastEventAt = now;
								eventCount++;
							}
						}
						catch (OperationCanceledException)
						{
							// expected: the deadline or scenario cancellation ended the read loop
						}

						metrics.RecordDisconnected(eventCount);
						return Response.Ok("200", eventCount);
					}
					catch (Exception ex) when (ex is not OperationCanceledException ||
					                           !ctx.ScenarioCancellationToken.IsCancellationRequested)
					{
						metrics.RecordDisconnected(eventCount);
						return Response.Fail(statusCode: ex.GetType().Name, message: ex.Message);
					}
				})
				.WithLoadSimulations(Simulation.KeepConstant(n, settings.Duration))
				// ponytail: no warm-up. Each iteration deliberately holds its connection open for
				// up to settings.Duration (potentially far longer than a short warm-up window) —
				// NBomber cancels warm-up iterations still in flight when the phase ends, and a
				// KeepConstant warm-up retries a canceled iteration immediately, so a long-held
				// connection spins into thousands of instant cancel-and-retry "failures" before
				// bombing even starts.
				.WithoutWarmUp()
				.WithMaxFailCount(settings.MaxFailCount)
				.WithClean(async _ =>
				{
					metrics.WriteReport(reportFolder, n1);
					// The anchor match/session is shared across every concurrency variant in this
					// loop; only the last one tears it down.
					if (n == settings.ConcurrentUsers[^1]) await anchorClient.DisposeAsync();
				});

			props.Add(scenario);
		}

		return props;
	}

	/// <summary>Logs in and creates a throwaway match, whose settings snapshot exists as soon as it's created.</summary>
	/// <returns>
	///     The new match's <b>database</b> id (what the REST <c>/matches/{id}/settings/live</c> endpoint
	///     keys on via <c>IMatchRegistry.GetByDbId</c>) — deliberately not the bancho-protocol match id
	///     from the <c>MatchJoinSuccess</c>/<c>NewMatch</c> packet (a separate, small 0-63 in-memory
	///     slot-pool index), or <see langword="null" /> if login, creation, or resolving the id failed.
	///     </summary>
	private static async Task<int?> CreateAnchorMatchAsync(BanchoClient client, LoadAccount account,
		BasilApiClient apiClient)
	{
		var outcome = await client.LoginAsync();
		if (!outcome.Success) return null;

		var slots = Enumerable.Range(0, 16)
			.Select(_ => new MatchSlotPacket(0, 0, 0, null))
			.ToArray();
		var match = new MatchPacket(
			0, false, 0, "sse-anchor", "",
			"", 0, "", slots, account.UserId ?? 0,
			0, 0, 0, false, 0);

		client.Send(ClientPacketWriter.CreateMatch(match));
		await client.PollAsync();

		// ponytail: the match list read can briefly lag the write; a few short retries beat
		// hard-coding a fixed pre-delay for every profile.
		for (var attempt = 0; attempt < 5; attempt++)
		{
			if (await apiClient.ResolveSampleMatchIdAsync() is { } matchId) return matchId;
			await Task.Delay(TimeSpan.FromMilliseconds(300));
		}

		return null;
	}

	private sealed class SseMetrics
	{
		private readonly List<double> _interEventGapMs = [];
		private readonly Lock _lock = new();
		private readonly List<double> _timeToFirstEventMs = [];
		private long _connected;
		private long _totalEvents;

		public void RecordConnected()
		{
			Interlocked.Increment(ref _connected);
		}

		public void RecordTimeToFirstEvent(double ms)
		{
			lock (_lock)
			{
				_timeToFirstEventMs.Add(ms);
			}
		}

		public void RecordInterEventGap(double ms)
		{
			lock (_lock)
			{
				_interEventGapMs.Add(ms);
			}
		}

		public void RecordDisconnected(int eventCount)
		{
			Interlocked.Add(ref _totalEvents, eventCount);
		}

		public void WriteReport(string reportFolder, int concurrency)
		{
			double[] timeToFirst;
			double[] interEvent;
			lock (_lock)
			{
				timeToFirst = [.. _timeToFirstEventMs];
				interEvent = [.. _interEventGapMs];
			}

			var lines = new List<string>
			{
				$"# SSE summary — {concurrency} concurrent subscribers",
				"",
				$"- Connections established: {Interlocked.Read(ref _connected)}",
				$"- Total events observed: {Interlocked.Read(ref _totalEvents)}"
			};

			if (timeToFirst.Length > 0)
			{
				Array.Sort(timeToFirst);
				lines.Add($"- Time-to-first-event p50: {timeToFirst[timeToFirst.Length / 2]:F1} ms");
			}

			if (interEvent.Length > 0)
			{
				Array.Sort(interEvent);
				lines.Add($"- Inter-event gap p50: {interEvent[interEvent.Length / 2]:F1} ms");
			}

			File.WriteAllLines(Path.Combine(reportFolder, $"sse-summary-{concurrency}.md"), lines);
		}
	}
}