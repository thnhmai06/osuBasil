using Basil.LoadTests.Client;
using Basil.LoadTests.Configuration;
using NBomber.Contracts;
using NBomber.CSharp;

namespace Basil.LoadTests.Scenarios;

/// <summary>
///     Benchmarks the commonly used read endpoints on the <c>api.</c> host: one scenario per endpoint
///     for clean per-endpoint percentiles, plus one <c>api_mixed</c> scenario blending them by
///     configured weight. Every path is resolved to a real, existing resource where one is available —
///     404 measures the wrong thing.
/// </summary>
public sealed class ApiScenario : IBasilScenario
{
	public string Id => "api";

	public IReadOnlyList<ScenarioProps> Build(BasilScenarioContext context)
	{
		var settings = context.GetScenarioSettings<ApiSettings>(Id);
		if (!settings.Enabled) return [];

		var clientFactory = context.ClientFactory;
		var apiClient = new BasilApiClient(clientFactory);

		// id 0 is the seeded BasilBot account, always present, used as a safe fallback target.
		var sampleUserId = context.Accounts.FirstOrDefault(a => a.UserId.HasValue)?.UserId ?? 0;
		var sampleMatchId = apiClient.ResolveSampleMatchIdAsync().GetAwaiter().GetResult() ?? 1;
		var sampleMapsetId = apiClient.ResolveSampleBeatmapsetIdAsync().GetAwaiter().GetResult() ?? 1;

		var paths = new Dictionary<string, string>
		{
			["health"] = "/health",
			["user"] = $"/users/{sampleUserId}",
			["match_list"] = "/matches?status=all&page=1&pageSize=20",
			["match_report"] = $"/matches/{sampleMatchId}",
			["beatmapset"] = $"/beatmapsets/{sampleMapsetId}"
		};

		var props = new List<ScenarioProps>();

		foreach (var n in settings.ConcurrentUsers)
		{
			foreach (var endpointId in settings.Endpoints)
			{
				if (!paths.TryGetValue(endpointId, out var path))
				{
					context.LogWarning($"'{Id}' endpoint '{endpointId}' is not recognized; skipping.");
					continue;
				}

				props.Add(BuildEndpointScenario($"{Id}_{endpointId}_{n}", clientFactory, [path], n, settings));
			}

			if (settings.MixedWeights.Count > 0)
			{
				var weightedPaths = settings.MixedWeights
					.Where(w => paths.ContainsKey(w.Key))
					.SelectMany(w => Enumerable.Repeat(paths[w.Key], Math.Max(1, w.Value)))
					.ToArray();

				if (weightedPaths.Length > 0)
					props.Add(BuildEndpointScenario($"{Id}_mixed_{n}", clientFactory, weightedPaths, n, settings));
			}
		}

		return props;
	}

	private static ScenarioProps BuildEndpointScenario(string name, BasilHttpClientFactory clientFactory,
		string[] weightedPaths, int concurrency, ApiSettings settings)
	{
		return Scenario.Create(name, async ctx =>
			{
				try
				{
					var path = weightedPaths.Length == 1
						? weightedPaths[0]
						: weightedPaths[Random.Shared.Next(weightedPaths.Length)];

					using var client = clientFactory.CreateClient();
					using var response =
						await client.GetAsync(clientFactory.BuildUri("api", path), ctx.ScenarioCancellationToken);
					var bytes = await response.Content.ReadAsByteArrayAsync(ctx.ScenarioCancellationToken);

					var statusCode = ((int)response.StatusCode).ToString();
					return response.IsSuccessStatusCode
						? Response.Ok(statusCode: statusCode, sizeBytes: bytes.Length)
						: Response.Fail(statusCode: statusCode);
				}
				catch (Exception ex) when (ex is not OperationCanceledException ||
				                            !ctx.ScenarioCancellationToken.IsCancellationRequested)
				{
					return Response.Fail(statusCode: ex.GetType().Name, message: ex.Message);
				}
			})
			.WithLoadSimulations(Simulation.KeepConstant(concurrency, settings.Duration))
			.WithWarmUpDuration(settings.WarmUp)
			.WithMaxFailCount(settings.MaxFailCount);
	}
}
