using System.Globalization;
using Basil.LoadTests.Client;
using Basil.LoadTests.Configuration;
using Basil.Protocol;
using Basil.Protocol.Packets;
using NBomber.Contracts;
using NBomber.CSharp;

namespace Basil.LoadTests.Scenarios;

/// <summary>
///     Chat throughput/latency workload: a configurable percentage of virtual users send channel
///     messages at a fixed rate, the rest only poll and receive. Because <c>GameSession</c>'s outbound
///     packet queue is unbounded, "dropped packets" is not a real metric here — a 0% drop figure would
///     be a tautology. Instead this measures delivery latency (marker timestamp embedded in each
///     message vs. receive time) and reports undelivered-at-end-of-run as the honest queue-depth proxy.
/// </summary>
public sealed class ChatScenario : IBasilScenario
{
	public string Id => "chat";

	public IReadOnlyList<ScenarioProps> Build(BasilScenarioContext context)
	{
		var settings = context.GetScenarioSettings<ChatSettings>(Id);
		if (!settings.Enabled) return [];

		var clientFactory = context.ClientFactory;
		var reportFolder = context.ReportFolder;
		var props = new List<ScenarioProps>();

		foreach (var n in settings.ConcurrentUsers)
		{
			var accounts = context.Accounts.Take(n).ToArray();
			if (accounts.Length < n)
				throw new InvalidOperationException(
					$"'{Id}' at concurrency {n} needs {n} seeded accounts but only {accounts.Length} exist; " +
					"increase Accounts:Count.");

			var senderCount = Math.Max(1, n * settings.SendersPercent / 100);
			var senderInterval = TimeSpan.FromSeconds(60.0 / Math.Max(1, settings.MessagesPerMinutePerSender));
			var filler = new string('x', Math.Max(0, settings.MessageBytes - 24));
			var metrics = new ChatMetrics();
			var n1 = n;

			var scenario = Scenario.Create($"{Id}_{n}", async ctx =>
				{
					try
					{
						var account = accounts[ctx.ScenarioInfo.InstanceNumber % accounts.Length];
						var isSender = ctx.ScenarioInfo.InstanceNumber < senderCount;

						if (!ctx.ScenarioInstanceData.TryGetValue("client", out var stored))
						{
							var client = new BanchoClient(clientFactory, account);
							var outcome = await client.LoginAsync(ctx.ScenarioCancellationToken);
							if (!outcome.Success)
								return Response.Fail(statusCode: outcome.FailureReason ?? "unknown-failure");

							if (!string.Equals(settings.Channel, "#osu", StringComparison.OrdinalIgnoreCase))
								client.Send(ClientPacketWriter.ChannelJoin(settings.Channel));

							ctx.ScenarioInstanceData["client"] = client;
							await client.PollAsync(ctx.ScenarioCancellationToken);
							return Response.Ok(statusCode: "login");
						}

						var existing = (BanchoClient)stored;

						if (isSender)
						{
							await Task.Delay(senderInterval, ctx.ScenarioCancellationToken);
							var marker = $"{metrics.NextSequence()}:{DateTimeOffset.UtcNow.Ticks}";
							var text = $"{marker}:{filler}";
							existing.Send(ClientPacketWriter.SendPublicMessage(
								new BanchoMessage(account.Name, text, settings.Channel, account.UserId ?? 0)));
							await existing.PollAsync(ctx.ScenarioCancellationToken);
							metrics.RecordSent();
							return Response.Ok(statusCode: "sent", sizeBytes: text.Length);
						}

						await Task.Delay(context.Profile.Client.PollInterval, ctx.ScenarioCancellationToken);
						var frames = await existing.PollAsync(ctx.ScenarioCancellationToken);
						var receivedBytes = 0;
						foreach (var frame in frames)
						{
							if (frame.Type != ServerPackets.SendMessage) continue;
							receivedBytes += frame.Payload.Length;

							var message = new PacketReader(frame.Payload).ReadMessage();
							var parts = message.Text.Split(':', 3);
							if (parts.Length < 2 ||
							    !long.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture,
								    out var sentTicks))
								continue;

							var latencyMs = (DateTimeOffset.UtcNow - new DateTimeOffset(sentTicks, TimeSpan.Zero))
								.TotalMilliseconds;
							metrics.RecordReceived(latencyMs);
						}

						return Response.Ok(statusCode: "polled", sizeBytes: receivedBytes);
					}
					catch (Exception ex) when (ex is not OperationCanceledException ||
					                            !ctx.ScenarioCancellationToken.IsCancellationRequested)
					{
						return Response.Fail(statusCode: ex.GetType().Name);
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

	/// <summary>Accumulates raw send/receive counters and latency samples for one concurrency level, since NBomber has no histogram metric to record them into directly.</summary>
	private sealed class ChatMetrics
	{
		private long _sequence;
		private long _sent;
		private long _received;
		private readonly List<double> _latenciesMs = [];
		private readonly Lock _lock = new();

		public long NextSequence()
		{
			return Interlocked.Increment(ref _sequence);
		}

		public void RecordSent()
		{
			Interlocked.Increment(ref _sent);
		}

		public void RecordReceived(double latencyMs)
		{
			Interlocked.Increment(ref _received);
			lock (_lock)
			{
				_latenciesMs.Add(latencyMs);
			}
		}

		public void WriteReport(string reportFolder, int concurrency)
		{
			double[] latencies;
			lock (_lock)
			{
				latencies = [.. _latenciesMs];
			}

			Array.Sort(latencies);
			var csvPath = Path.Combine(reportFolder, $"chat-latency-{concurrency}.csv");
			using (var writer = new StreamWriter(csvPath))
			{
				writer.WriteLine("LatencyMs");
				foreach (var latency in latencies) writer.WriteLine(latency.ToString(CultureInfo.InvariantCulture));
			}

			var sent = Interlocked.Read(ref _sent);
			var received = Interlocked.Read(ref _received);
			var summaryPath = Path.Combine(reportFolder, $"chat-summary-{concurrency}.md");
			var lines = new List<string>
			{
				$"# Chat summary — {concurrency} concurrent users",
				"",
				$"- Sent: {sent}",
				$"- Received (with a recognizable delivery marker): {received}",
				$"- Undelivered at end of run: {Math.Max(0, sent - received)}",
				$"- Note: the outbound packet queue is unbounded, so packets are never dropped by design — " +
				"this figure reflects timing (message sent near the run's end, or a receiver's last poll " +
				"missing it), not loss."
			};

			if (latencies.Length > 0)
			{
				lines.Add($"- Delivery latency p50: {Percentile(latencies, 0.50):F1} ms");
				lines.Add($"- Delivery latency p95: {Percentile(latencies, 0.95):F1} ms");
				lines.Add($"- Delivery latency p99: {Percentile(latencies, 0.99):F1} ms");
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
