// Drives a running Basil server through nginx with osu!, IRC and API clients and reports requests, failures, latency
// percentiles and throughput per kind of client. See PERF.md in the repository root for the scenario and how to run it.
//
//   dotnet run --project tools/LoadHarness -c Release -- --accounts accounts.csv --osu 200 --irc 50 --api 20 --insecure
using System.Diagnostics;
using System.Text.Json;
using Basil.Protocol.Game.Stable.Models.Authentication;
using LoadHarness;

Options options;
List<Account> accounts;
try
{
	if (args.Contains("--help") || args.Length == 0)
	{
		Console.WriteLine(Options.Usage);
		return 0;
	}

	options = Options.Parse(args);
	accounts = Account.Load(options.Accounts);
	var needed = options.Osu + options.Irc + options.Api;
	if (accounts.Count < needed)
		throw new ArgumentException($"{needed} clients need {needed} accounts; {options.Accounts} has {accounts.Count}.");

	// The login body is built here and read back by the server's own parser, so a wire change fails at once.
	_ = LoginRequest.Parse(OsuClient.LoginBody(accounts[0], 0));
}
catch (Exception exception) when (exception is ArgumentException or FormatException or IOException)
{
	Console.Error.WriteLine(exception.Message);
	return 2;
}

var recorder = new Recorder();
var context = new Context(options, recorder);
using var cancel = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
	e.Cancel = true;
	cancel.Cancel();
};

var clients = new List<Func<CancellationToken, Task>>();
var next = 0;
for (var i = 0; i < options.Osu; i++)
{
	var client = new OsuClient(context, accounts[next++], i);
	clients.Add(client.RunAsync);
}

for (var i = 0; i < options.Irc; i++)
{
	var client = new IrcClient(context, accounts[next++]);
	clients.Add(client.RunAsync);
}

for (var i = 0; i < options.Api; i++)
{
	var client = new ApiClient(context, accounts[next++]);
	clients.Add(client.RunAsync);
}

// The kinds start side by side, each client at its own moment of the ramp.
var ct = cancel.Token;
var order = clients.Select((run, i) => (run, i)).OrderBy(_ => Random.Shared.Next()).ToList();
var tasks = order.Select((entry, slot) => Task.Run(async () =>
{
	try
	{
		await Task.Delay(options.Ramp * slot / clients.Count, ct);
		await entry.run(ct);
	}
	catch (OperationCanceledException)
	{
	}
})).ToList();

var sampler = options.Container is null ? null : new ContainerSampler(options.Container);
var sampling = sampler?.RunAsync(recorder, ct) ?? Task.CompletedTask;
var startedAt = DateTimeOffset.UtcNow;
Console.WriteLine($"Ramping {clients.Count} clients over {options.Ramp.TotalSeconds:0.#} s, then measuring {options.Duration.TotalSeconds:0.#} s.");

try
{
	await Task.Delay(options.Ramp, ct);
	recorder.StartMeasuring();
	await Task.Delay(options.Duration, ct);
}
catch (OperationCanceledException)
{
}

recorder.StopMeasuring();
var measured = recorder.Elapsed;
cancel.Cancel();
await Task.WhenAll(tasks);
await sampling;

var report = recorder.Report(measured);
var container = sampler?.Report();
Print(report, container, measured);
await File.WriteAllTextAsync(options.Out, JsonSerializer.Serialize(new
{
	startedAt,
	measuredSeconds = Math.Round(measured.TotalSeconds, 1),
	scenario = new
	{
		options.Domain,
		options.Osu,
		options.Irc,
		options.Api,
		durationSeconds = options.Duration.TotalSeconds,
		rampSeconds = options.Ramp.TotalSeconds,
		osuPollSeconds = options.OsuPoll.TotalSeconds,
		ircIntervalSeconds = options.IrcInterval.TotalSeconds,
		apiIntervalSeconds = options.ApiInterval.TotalSeconds
	},
	kinds = report,
	container
}, new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
Console.WriteLine($"Result written to {options.Out}");
return report.Values.Any(kind => kind.Failed > 0) ? 1 : 0;

static void Print(Dictionary<string, KindReport> report, ContainerReport? container, TimeSpan measured)
{
	Console.WriteLine();
	Console.WriteLine($"Measured {measured.TotalSeconds:0.#} s");
	Console.WriteLine($"{"kind",-14}{"requests",10}{"failed",8}{"p50 ms",10}{"p95 ms",10}{"p99 ms",10}{"req/s",9}");
	foreach (var (kind, row) in report)
		Console.WriteLine($"{kind,-14}{row.Requests,10}{row.Failed,8}{row.P50Ms,10:0.##}{row.P95Ms,10:0.##}{row.P99Ms,10:0.##}{row.RequestsPerSecond,9:0.#}");

	foreach (var (kind, row) in report)
		foreach (var (reason, count) in row.FailuresByReason)
			Console.WriteLine($"  {kind}: {count} x {reason}");

	if (container is not null)
		Console.WriteLine(
			$"{container.Container}: CPU avg {container.CpuPercentAverage}% max {container.CpuPercentMax}%, memory avg {container.MemoryMiBAverage} MiB max {container.MemoryMiBMax} MiB ({container.Samples} samples)");
}
