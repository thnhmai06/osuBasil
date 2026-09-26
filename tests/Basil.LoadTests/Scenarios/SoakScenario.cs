using System.Collections.Concurrent;
using Basil.LoadTests.Client;
using Basil.LoadTests.Configuration;
using Basil.Protocol;
using Basil.Protocol.Multiplayer;
using NBomber.Contracts;
using NBomber.CSharp;

namespace Basil.LoadTests.Scenarios;

/// <summary>
///     Long-running weighted mix of the Phase 2 workloads, held for hours rather than minutes, watching
///     for memory/thread/handle growth via <see cref="Analysis.SoakAnalyzer" /> after the run.
/// </summary>
/// <remarks>
///     Each virtual user holds one persistent login and rolls a weighted action every iteration rather
///     than being permanently assigned a role — over the run's duration this converges to the
///     configured mix without needing cross-instance room coordination. The "multiplayer" bucket is a
///     solo create→part cycle (exercises the same packet/DB paths without needing other virtual users
///     to synchronize into the same room over many hours unattended) rather than <see cref="MultiplayerScenario" />'s
///     full multi-player room lifecycle. The "sse" bucket holds a subscriber on a match's live stream
///     through that same match closing and a second one opening, to exercise the close/reopen churn a
///     short soak can't observe over hours of handle/thread growth.
/// </remarks>
public sealed class SoakScenario : IBasilScenario
{
	public string Id => "soak";

	public IReadOnlyList<ScenarioProps> Build(BasilScenarioContext context)
	{
		var settings = context.GetScenarioSettings<SoakSettings>(Id);
		if (!settings.Enabled || settings.ConcurrentUsers.Length == 0) return [];

		var clientFactory = context.ClientFactory;
		var accounts = context.Accounts;
		var pollInterval = context.Profile.Client.PollInterval;
		var weightedActions = BuildWeightedActions(settings.Weights);
		var clients = new ConcurrentBag<BanchoClient>();
		var apiClient = new BasilApiClient(clientFactory);

		var n = settings.ConcurrentUsers[0];
		if (accounts.Count < n)
			throw new InvalidOperationException(
				$"'{Id}' needs {n} seeded accounts but only {accounts.Count} exist; increase Accounts:Count.");

		var scenario = Scenario.Create($"{Id}_{n}", async ctx =>
			{
				try
				{
					var account = accounts[ctx.ScenarioInfo.InstanceNumber % accounts.Count];

					if (!ctx.ScenarioInstanceData.TryGetValue("client", out var stored))
					{
						var newClient = new BanchoClient(clientFactory, account);
						var outcome = await newClient.LoginAsync(ctx.ScenarioCancellationToken);
						if (!outcome.Success)
							return Response.Fail(message: outcome.FailureReason ?? "unknown-failure");

						// Server-side channel membership is never registered by login alone (the auto-join
						// bundle only tells the client which channels to join itself) — the chat action
						// below writes to #osu, so this join is required or every send is silently dropped.
						newClient.Send(ClientPacketWriter.ChannelJoin("#osu"));
						await newClient.PollAsync(ctx.ScenarioCancellationToken);

						clients.Add(newClient);
						ctx.ScenarioInstanceData["client"] = newClient;
						return Response.Ok(statusCode: "login");
					}

					var client = (BanchoClient)stored;
					var action = weightedActions.Count == 0
						? "idle"
						: weightedActions[Random.Shared.Next(weightedActions.Count)];

					await Task.Delay(pollInterval, ctx.ScenarioCancellationToken);

					switch (action)
					{
						case "chat":
							client.Send(ClientPacketWriter.SendPublicMessage(
								new BanchoMessage(account.Name, $"soak:{DateTimeOffset.UtcNow.Ticks}", "#osu",
									account.UserId ?? 0)));
							await client.PollAsync(ctx.ScenarioCancellationToken);
							return Response.Ok(statusCode: "chat");

						case "multiplayer":
							var match = new MatchPacket(0, false, 0, $"soak-{ctx.ScenarioInfo.InstanceNumber}", "",
								"", 0, "",
								[.. Enumerable.Range(0, 16).Select(_ => new MatchSlotPacket(0, 0, 0, null))],
								account.UserId ?? 0, 0, 0, 0, false, 0);
							client.Send(ClientPacketWriter.CreateMatch(match));
							await client.PollAsync(ctx.ScenarioCancellationToken);
							client.Send(ClientPacketWriter.PartMatch());
							await client.PollAsync(ctx.ScenarioCancellationToken);
							return Response.Ok(statusCode: "multiplayer");

						case "sse":
						{
							IReadOnlyList<MatchSlotPacket> slots =
								[.. Enumerable.Range(0, 16).Select(_ => new MatchSlotPacket(0, 0, 0, null))];
							var roomName = $"soak-sse-{ctx.ScenarioInfo.InstanceNumber}-{DateTimeOffset.UtcNow.Ticks}";
							var sseMatch = new MatchPacket(0, false, 0, roomName, "", "", 0, "", slots,
								account.UserId ?? 0, 0, 0, 0, false, 0);
							client.Send(ClientPacketWriter.CreateMatch(sseMatch));
							await client.PollAsync(ctx.ScenarioCancellationToken);

							// The match-list read can briefly lag the create write (same gap SseScenario's own
							// anchor-match setup retries around).
							int? matchId = null;
							for (var attempt = 0; attempt < 5 && matchId is null; attempt++)
							{
								matchId = await apiClient.ResolveMatchIdByNameAsync(roomName,
									ctx.ScenarioCancellationToken);
								if (matchId is null)
									await Task.Delay(TimeSpan.FromMilliseconds(200), ctx.ScenarioCancellationToken);
							}

							if (matchId is null)
							{
								client.Send(ClientPacketWriter.PartMatch());
								await client.PollAsync(ctx.ScenarioCancellationToken);
								return Response.Fail(statusCode: "sse-no-match-id");
							}

							// Bounded independently of the soak's own multi-hour duration: a stream that never
							// completes must not wedge this virtual user for the rest of the run.
							using var deadline =
								CancellationTokenSource.CreateLinkedTokenSource(ctx.ScenarioCancellationToken);
							deadline.CancelAfter(TimeSpan.FromSeconds(15));

							var streamClosedByServer = false;
							try
							{
								using var http = clientFactory.CreateClient();
								http.Timeout = Timeout.InfiniteTimeSpan;
								using var response = await http.GetAsync(
									clientFactory.BuildUri("api", $"/matches/{matchId}/settings/live"),
									HttpCompletionOption.ResponseHeadersRead, deadline.Token);
								if (!response.IsSuccessStatusCode)
								{
									client.Send(ClientPacketWriter.PartMatch());
									await client.PollAsync(ctx.ScenarioCancellationToken);
									return Response.Fail(statusCode: ((int)response.StatusCode).ToString());
								}

								await using var stream = await response.Content.ReadAsStreamAsync(deadline.Token);
								using var reader = new StreamReader(stream);

								// The headers only flush once a snapshot exists to send (see this class's own
								// SSE remarks), so getting this far already confirms the subscription is live --
								// no need to inspect the buffered snapshot content itself before churning.

								// The close/reopen churn RC5 is about: close the match this stream is
								// subscribed to, then create and close a second one, all while the original
								// stream stays connected throughout. Closed via the admin API's
								// `POST /matches/{id}/close`, not `PartMatch` -- a player merely leaving
								// doesn't tear the room down at all, it just leaves it empty for the
								// documented 15-minute empty-room timer (see multiplayer.md), which is far
								// longer than any bounded soak-iteration deadline can wait on.
								using (var closeResponse = await http.PostAsync(
									       clientFactory.BuildUri("api", $"/matches/{matchId}/close"),
									       content: null, deadline.Token))
								{
									if (!closeResponse.IsSuccessStatusCode)
										return Response.Fail(statusCode: $"sse-close-{(int)closeResponse.StatusCode}");
								}

								var reopenMatch = new MatchPacket(0, false, 0, $"{roomName}-reopen", "", "", 0, "",
									slots, account.UserId ?? 0, 0, 0, 0, false, 0);
								client.Send(ClientPacketWriter.CreateMatch(reopenMatch));
								await client.PollAsync(ctx.ScenarioCancellationToken);
								client.Send(ClientPacketWriter.PartMatch());
								await client.PollAsync(ctx.ScenarioCancellationToken);

								// The RC5 signal: does the ORIGINAL stream (subscribed to the now-closed
								// match) ever complete on its own -- ADR-004's Writer.Complete() firing on
								// teardown -- or does it hang until only our own deadline forces it closed. A
								// stream that only ever ends via cancellation, never on its own, is the leak
								// signature to watch for across the whole soak run's handle count.
								//
								// A single ReadLineAsync isn't enough to tell: the SSE wire format is multiple
								// lines per event ("event: ...", "data: ...", then a blank terminator line), so
								// a still-buffered leftover line from the snapshot already read into the
								// StreamReader would read back non-null and be misread as "still open" even
								// after the server has actually completed the channel. Drain every remaining
								// line instead and only conclude "closed" on a genuine EOF (null).
								while (true)
								{
									var line = await reader.ReadLineAsync(deadline.Token);
									if (line is not null) continue;
									streamClosedByServer = true;
									break;
								}
							}
							catch (OperationCanceledException) when (deadline.IsCancellationRequested)
							{
								// Expected when the stream never completed on its own within the bounded
								// window -- streamClosedByServer stays false, which is exactly the signal.
							}

							return Response.Ok(statusCode: streamClosedByServer ? "sse-closed" : "sse-still-open");
						}

						case "api":
							using (var http = clientFactory.CreateClient())
							{
								var uri = clientFactory.BuildUri("api", $"/users/{account.UserId ?? 0}");
								using var response = await http.GetAsync(uri, ctx.ScenarioCancellationToken);
								return response.IsSuccessStatusCode
									? Response.Ok(statusCode: "200")
									: Response.Fail(statusCode: ((int)response.StatusCode).ToString());
							}

						default:
							await client.PollAsync(ctx.ScenarioCancellationToken);
							return Response.Ok(statusCode: "idle");
					}
				}
				catch (Exception ex) when (ex is not OperationCanceledException ||
				                           !ctx.ScenarioCancellationToken.IsCancellationRequested)
				{
					// A 12-24h unattended run must never let one transient failure propagate as an
					// unhandled exception — always resolve to a counted failure and keep going.
					return Response.Fail(statusCode: ex.GetType().Name, message: ex.Message);
				}
			})
			.WithLoadSimulations(Simulation.KeepConstant(n, settings.Duration))
			// ponytail: see ChatScenario.cs — a warm-up phase running this same login-and-hold
			// step leaves a live session bombing's fresh copies then can never log back into.
			.WithoutWarmUp()
			.WithMaxFailCount(int.MaxValue)
			.WithClean(async _ =>
			{
				foreach (var client in clients) await client.DisposeAsync();
			});

		// NBomberRunner.WithReportingInterval (applied per-run, not per-scenario) is set from
		// this same SoakSettings.ReportingInterval by Program.cs when it runs this scenario, so a
		// multi-hour run streams interim stats instead of reporting only once at the end.
		return [scenario];
	}

	private static List<string> BuildWeightedActions(IReadOnlyDictionary<string, int> weights)
	{
		var list = new List<string>();
		foreach (var (action, weight) in weights)
			for (var i = 0; i < Math.Max(1, weight); i++)
				list.Add(action);

		return list;
	}
}