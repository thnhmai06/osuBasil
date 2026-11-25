using System.Text;
using System.Text.Json;
using Basil.Tests.LoadTests;
using Basil.Tests.LoadTests.Capacity;
using Basil.Tests.LoadTests.Load;
using Basil.Tests.LoadTests.Scalability;
using Basil.Tests.LoadTests.Smoke;
using Basil.Tests.LoadTests.Soak;
using Basil.Tests.LoadTests.Spike;
using Basil.Tests.LoadTests.Stress;
using Basil.Tests.Shared.Clients;
using Basil.Tests.Shared.Environment.Monitor;
using Basil.Tests.Shared.Environment.Seed;
using Basil.Tests.Shared.Workload.Outcomes;
using Basil.Tests.Shared.Workload.Personas;
using Basil.Tests.Shared.Workload.World;
using NBomber.Contracts.Stats;
using NBomber.CSharp;

var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true };

LoadTestOptions options;
try
{
	options = LoadTestOptions.Parse(args);
}
catch (ArgumentException ex)
{
	PrintUsage(ex.Message == "help" ? null : ex.Message);
	return ex.Message == "help" ? 0 : 2;
}

if (options.Test == "pilot")
	options = options with { Test = "capacity", Pilot = true };

using var http = Endpoint.CreateHttpClient(TimeSpan.FromSeconds(30));

if (options.Test == "seed")
{
	if (string.IsNullOrEmpty(options.OwnerPassword))
	{
		Console.Error.WriteLine("--owner-pass is required for seed.");
		return 2;
	}

	var seeder = new Seeder(http);
	var report = await seeder.RunAsync(new SeedOptions(
		options.Endpoint,
		options.OwnerUser,
		options.OwnerPassword,
		options.AccountsPath,
		options.WorldPath,
		options.PlayerCount,
		options.RefereeCount,
		options.TourneyCount,
		options.ExpectedBeatmapsets,
		options.Seed), CancellationToken.None);

	Console.WriteLine(JsonSerializer.Serialize(report, jsonOptions));
	return report.RateLimitRestored ? 0 : 1;
}

var worldData = WorldData.Load(options.WorldPath);
var accounts = WorldData.LoadAccounts(options.AccountsPath);
var timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss");

if (options.Test == "control")
{
	var account = accounts.FirstOrDefault(a => a.Role == AccountRole.Player)
	              ?? throw new InvalidOperationException("No player account found in accounts.csv for control probe.");
	var map = worldData.AllBeatmaps.FirstOrDefault()
	          ?? throw new InvalidOperationException("No beatmaps found in world-data.json for control probe.");

	var controlDir = Path.Combine(options.ReportsRoot, $"control-{timestamp}");
	var probe = new ControlProbe(options.Endpoint, options.IrcPort, http, account.Name, account.Password,
		map.Md5, map.Mode);
	using var cts = new CancellationTokenSource();
	Console.CancelKeyPress += (_, e) =>
	{
		e.Cancel = true;
		cts.Cancel();
	};
	await probe.RunAsync(options.SteadyDuration, TimeSpan.FromSeconds(2), controlDir, cts.Token);
	Console.WriteLine($"Control trace written to {controlDir}");
	return 0;
}

ILoadTest[] tests =
[
	new LoadTest(),
	new StressTest(),
	new SpikeTest(),
	new SoakTest(),
	new CapacityTest(),
	new ScalabilityTest(),
	new SmokeTest()
];

var selected = tests.FirstOrDefault(t => t.Name == options.Test);
if (selected is null)
{
	PrintUsage($"Unknown test '{options.Test}'.");
	return 2;
}

var reportName = selected.ReportName(options);
var reportFolder = Path.Combine(options.ReportsRoot, $"{reportName}-{timestamp}");
Directory.CreateDirectory(reportFolder);

var pool = new AccountPool(accounts);
var worldState = new WorldState();
var outcomes = new OutcomeTally();
using var runAbort = new CancellationTokenSource();
var lockoutGuard = new LoginLockoutGuard(runAbort);

// In Spike mode, align all tournament rooms on one shared start instant right after ramp.
var tournamentClock = options.Test == "spike"
	? new TournamentClock(DateTimeOffset.UtcNow + options.RampDuration(options.TargetSessions) + TimeSpan.FromSeconds(15))
	: new TournamentClock();

var workload = new WorkloadContext(
	options.Endpoint,
	options.IrcPort,
	http,
	worldData,
	pool,
	worldState,
	outcomes,
	lockoutGuard,
	tournamentClock,
	options.Seed,
	runAbort.Token)
{
	RoundDuration = options.RoundDuration,
	PollInterval = options.PollInterval
};

var developer = accounts.FirstOrDefault(a => a.Role == AccountRole.Developer);
await using var monitor = developer is null
	? null
	: new DiagnosticsMonitor(options.Endpoint, http, developer.Name, developer.Password, reportFolder);

var scenarios = selected.BuildScenarios(options, workload, monitor);

var stats = NBomberRunner
	.RegisterScenarios([.. scenarios])
	.WithReportFileName(reportName)
	.WithReportFolder(reportFolder)
	.WithReportFormats(ReportFormat.Html, ReportFormat.Csv, ReportFormat.Md, ReportFormat.Txt)
	.Run();

var serverSummary = monitor?.Summary();
var failedThresholds = stats.Thresholds
	.Where(t => t.IsFailed)
	.Select(t => new { t.ScenarioName, t.StepName, t.CheckExpression, t.ErrorCount })
	.ToArray();

var meta = new
{
	test = selected.Name,
	pilot = options.Pilot,
	reportName,
	startedAtUtc = timestamp,
	seed = options.Seed,
	endpoint = options.Endpoint,
	ircPort = options.IrcPort,
	targetSessions = options.TargetSessions,
	basilCores = options.BasilCores,
	lockoutAborted = lockoutGuard.IsTripped,
	outcomes = outcomes.Snapshot(),
	totals = new
	{
		ok = outcomes.Total(Outcome.Ok),
		refused = outcomes.Total(Outcome.Refused),
		throttled = outcomes.Total(Outcome.Throttled),
		shed = outcomes.Total(Outcome.Shed),
		error = outcomes.Total(Outcome.Error)
	},
	server = serverSummary,
	failedThresholds
};

await File.WriteAllTextAsync(
	Path.Combine(reportFolder, "run-meta.json"),
	JsonSerializer.Serialize(meta, jsonOptions),
	new UTF8Encoding(false));

Console.WriteLine($"Report folder: {reportFolder}");
Console.WriteLine($"Seed: {options.Seed} | Errors: {outcomes.Total(Outcome.Error)} | Failed thresholds: {failedThresholds.Length}");
if (lockoutGuard.IsTripped)
	Console.Error.WriteLine("ABORTED: Login lockout guard tripped on refused login burst.");

return failedThresholds.Length > 0 || lockoutGuard.IsTripped || (serverSummary?.RestartDetected ?? false) ? 1 : 0;

static void PrintUsage(string? error)
{
	if (!string.IsNullOrEmpty(error)) Console.Error.WriteLine(error);
	Console.WriteLine("""
	                  Usage: Basil.Tests.LoadTests <command> [options]

	                  Commands:
	                    seed          Create seeded accounts and world-data.json via the public REST API
	                    smoke         T8 smoke run (two 2v2 rooms + 20 free players + edge cases, 2 min)
	                    pilot         Short capacity run to determine ceiling N
	                    load          Closed-model load test at steady N with SLO thresholds
	                    stress        Steps 1x to 2x N then drops to 0.5x to measure recovery
	                    spike         Background load + synchronized tournament start wave
	                    soak          70% of N for 6 hours in per-hour windows
	                    capacity      Steps of +ΔN every 5 minutes until SLO breaks
	                    scalability   Capacity run tagged with --cores (1, 2 or 4)
	                    control       Steady low-rate control probe (for espresso)

	                  Options:
	                    --connect <ip>         Target host IP (default: 192.168.27.124)
	                    --port <port>          HTTPS port (default: 8443)
	                    --irc-port <port>      IRC TLS port (default: 6697)
	                    --domain <domain>      Server domain (default: basil.local)
	                    --accounts <file>      Path to accounts.csv (default: accounts.csv)
	                    --world <file>         Path to world-data.json (default: world-data.json)
	                    --reports <dir>        Report output directory (default: reports)
	                    --n <sessions>         Target concurrent sessions N (default: 100)
	                    --step-delta <n>       Capacity step increment (default: 50)
	                    --steps <count>        Capacity step count (default: 6)
	                    --step-minutes <min>   Minutes per step (default: 5)
	                    --cores <n>            Basil pinned core count (default: 4)
	                    --seed <int>           Fixed RNG seed (default: 20261125)
	                    --duration <dur>       Steady/control duration, e.g. 10m, 2m, 30s
	                    --soak-duration <dur>  Soak duration, e.g. 6h
	                    --owner-user <name>    Owner username for seed (default: admin)
	                    --owner-pass <pass>    Owner password for seed
	                    --players <n>          Player accounts to seed (default: 1500)
	                    --referees <n>         Referee accounts to seed (default: 100)
	                    --tourneys <n>         Tourney accounts to seed (default: 400)
	                    --expected-sets <n>    Expected beatmapsets for seed wait (default: 15)
	                  """);
}
