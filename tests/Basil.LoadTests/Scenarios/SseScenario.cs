using Basil.LoadTests.Configuration;
using NBomber.Contracts;
using NBomber.CSharp;

namespace Basil.LoadTests.Scenarios;

/// <summary>
///     Holds N concurrent subscribers open on a <c>.../live</c> Server-Sent Events endpoint for the
///     scenario's duration. Unlike ordinary GETs, these connections are long-lived and pin server
///     resources for as long as they're held — the honest place to watch handle/thread growth vs.
///     subscriber count (read from the shared resource timeline, not this scenario).
/// </summary>
public sealed class SseScenario : IBasilScenario
{
	public string Id => "sse";

	public IReadOnlyList<ScenarioProps> Build(BasilScenarioContext context)
	{
		var settings = context.GetScenarioSettings<SseSettings>(Id);
		if (!settings.Enabled) return [];

		var clientFactory = context.ClientFactory;
		var sampleUserId = context.Accounts.FirstOrDefault(a => a.UserId.HasValue)?.UserId ?? 0;
		var reportFolder = context.ReportFolder;

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
							clientFactory.BuildUri("api", $"/users/{sampleUserId}/live"),
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
						return Response.Ok(statusCode: "200", eventCount);
					}
					catch (Exception ex) when (ex is not OperationCanceledException ||
					                           !ctx.ScenarioCancellationToken.IsCancellationRequested)
					{
						metrics.RecordDisconnected(eventCount);
						return Response.Fail(statusCode: ex.GetType().Name, message: ex.Message);
					}
				})
				.WithLoadSimulations(Simulation.KeepConstant(n, settings.Duration))
				.WithWarmUpDuration(settings.WarmUp)
				.WithMaxFailCount(settings.MaxFailCount)
				.WithClean(_ =>
				{
					metrics.WriteReport(reportFolder, n1);
					return Task.CompletedTask;
				});

			props.Add(scenario);
		}

		return props;
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