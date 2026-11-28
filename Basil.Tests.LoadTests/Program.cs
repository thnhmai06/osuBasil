using System.Globalization;
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
using Basil.Tests.Shared.Environment.Checks;
using Basil.Tests.Shared.Environment.Monitor;
using Basil.Tests.Shared.Environment.Seed;
using Basil.Tests.Shared.Workload.Outcomes;
using Basil.Tests.Shared.Workload.Personas;
using Basil.Tests.Shared.Workload.World;
using NBomber.Contracts.Stats;
using NBomber.CSharp;

// Exit codes: 0 the SLO held, 3 the run was sound and the SLO broke, 1 the run itself broke (bad options, lockout
// guard, server restart, crash). Capacity, pilot and stress are meant to break the SLO, so the runner treats 3 as success there.
const int exitSloMet = 0;
const int exitRunBroken = 1;
const int exitSloBroken = 3;

var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true };

var planOnly = args.Length > 1 && args[0] == "plan";
LoadTestOptions options;
try
{
	options = LoadTestOptions.Parse(planOnly ? args[1..] : args);
}
catch (Exception ex) when (ex is ArgumentException or FormatException or OverflowException)
{
	PrintUsage(ex.Message == "help" ? null : ex.Message);
	return ex.Message == "help" ? 0 : exitRunBroken;
}

if (options.Test == "pilot")
	options = options with { Test = "capacity", Pilot = true };

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

if (planOnly)
{
	// Prints the planned duration in whole seconds, so the runner can size the control probe to the same length.
	if (selected is null)
	{
		PrintUsage($"Unknown test '{options.Test}'.");
		return exitRunBroken;
	}

	Console.WriteLine((long)Math.Ceiling(selected.Plan(options).Total.TotalSeconds));
	return 0;
}

using var http = Endpoint.CreateHttpClient(TimeSpan.FromSeconds(30));

if (options.Test == "seed")
{
	if (string.IsNullOrEmpty(options.OwnerPassword))
	{
		Console.Error.WriteLine(
			$"The owner password is required for seed: set {LoadTestOptions.OwnerPasswordVariable}.");
		return exitRunBroken;
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
	return report.RateLimitRestored ? 0 : exitRunBroken;
}

var worldData = WorldData.Load(options.WorldPath);
var accounts = WorldData.LoadAccounts(options.AccountsPath);
var timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);

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

if (selected is null)
{
	PrintUsage($"Unknown test '{options.Test}'.");
	return exitRunBroken;
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
	? new TournamentClock(DateTimeOffset.UtcNow + options.RampDuration(options.TargetSessions) +
	                      TimeSpan.FromSeconds(15))
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

var plan = selected.Plan(options);
var runStartedAt = DateTimeOffset.UtcNow;
NodeStats stats;
try
{
	stats = NBomberRunner
		.RegisterScenarios([.. scenarios])
		.WithReportFileName(reportName)
		.WithReportFolder(reportFolder)
		.WithReportFormats(ReportFormat.Html, ReportFormat.Csv, ReportFormat.Md, ReportFormat.Txt)
		.Run();
}
catch (Exception ex)
{
	Console.Error.WriteLine($"CRASH: {ex}");
	await File.WriteAllTextAsync(Path.Combine(reportFolder, "run-meta.json"),
		JsonSerializer.Serialize(new { test = selected.Name, crashed = true, error = ex.ToString() }, jsonOptions),
		new UTF8Encoding(false));
	return exitRunBroken;
}

// Plan T8: the server-side checks of the smoke run, made only through the public REST API.
IReadOnlyList<CheckResult> checks = [];
if (selected.Name == "smoke")
{
	var submitted = outcomes.Snapshot().GetValueOrDefault(Channels.Submit)?.GetValueOrDefault("ok") ?? 0;
	try
	{
		checks = developer is null
			? [new CheckResult("sign-in", false, "accounts.csv has no developer account for the checks.")]
			: await new SmokeChecks(options.Endpoint, http, developer.Name, developer.Password)
				.RunAsync(runStartedAt, submitted, SmokeTest.Rooms, monitor, TimeSpan.FromSeconds(30),
					CancellationToken.None);
	}
	catch (Exception ex)
	{
		checks = [new CheckResult("server-side checks", false, ex.Message)];
	}
}

var serverSummary = monitor?.Summary();
var failedThresholds = stats.Thresholds
	.Where(t => t.IsFailed)
	.Select(t => new { t.ScenarioName, t.StepName, t.CheckExpression, t.ErrorCount })
	.ToArray();

// CPU is also judged after the run from the monitor, so the verdict does not depend on one sampled value.
var judgesSlo = options.Test is not ("stress" or "spike");
// Judged only while a step holds its load; the peak of the whole run, ramps included, is reported beside it.
var loadStart = runStartedAt + plan.WarmUp;
var steadyCpu = plan.Steps.Select(step => new
{
	step.Index,
	step.Name,
	step.TargetSessions,
	p95CpuPercent = monitor?.CpuP95Between(loadStart + step.SteadyStart + SloThresholds.SteadyMargin,
		loadStart + step.End)
}).ToArray();
var worstStepCpu = steadyCpu.Max(step => step.p95CpuPercent);
var cpuBroken = judgesSlo && worstStepCpu > SloThresholds.MaxServerCpuPercent;
var checksBroken = checks.Any(c => !c.Passed);
var sloBroken = failedThresholds.Length > 0 || cpuBroken || checksBroken;
var restarted = serverSummary?.RestartDetected ?? false;
var runBroken = lockoutGuard.IsTripped || restarted;
var exitCode = runBroken ? exitRunBroken : sloBroken ? exitSloBroken : exitSloMet;

var meta = new
{
	test = selected.Name,
	pilot = options.Pilot,
	reportName,
	startedAtUtc = timestamp,
	runStartedAt,
	seed = options.Seed,
	endpoint = options.Endpoint,
	ircPort = options.IrcPort,
	targetSessions = options.TargetSessions,
	basilCores = options.BasilCores,
	lockoutAborted = lockoutGuard.IsTripped,
	serverRestarted = restarted,
	verdict = new
	{
		exitCode,
		sloMet = !sloBroken,
		runBroken,
		failedThresholds = failedThresholds.Length,
		cpuMaxPercent = serverSummary?.MaxCpuPercent,
		cpuSteadyP95Percent = worstStepCpu,
		cpuSteps = steadyCpu,
		cpuLimitPercent = SloThresholds.MaxServerCpuPercent,
		cpuBroken
	},
	// Measured by the server's diagnostics: the sessions it really held beside the target N.
	usersOnline = serverSummary is null
		? null
		: new
		{
			target = options.TargetSessions, peak = serverSummary.PeakUsersOnline,
			baseline = serverSummary.BaselineUsersOnline, final = serverSummary.FinalUsersOnline
		},
	// Offsets count from the end of the warm-up, which starts at runStartedAt plus the scenario init; split
	// the NBomber timeline and diagnostics.jsonl (it stamps every sample with its UTC time) with them.
	plan = new
	{
		warmUpSeconds = plan.WarmUp.TotalSeconds,
		tailSeconds = plan.Tail.TotalSeconds,
		totalSeconds = plan.Total.TotalSeconds,
		steps = plan.Steps.Select(step => new
		{
			step.Index,
			step.Name,
			step.TargetSessions,
			startSeconds = step.Start.TotalSeconds,
			steadyStartSeconds = step.SteadyStart.TotalSeconds,
			endSeconds = step.End.TotalSeconds
		})
	},
	accountPoolMisses = pool.Misses,
	serverChecks = checks,
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
Console.WriteLine(
	$"Seed: {options.Seed} | Errors: {outcomes.Total(Outcome.Error)} | Failed thresholds: {failedThresholds.Length} | Exit: {exitCode}");
foreach (var check in checks)
	Console.WriteLine($"Check {(check.Passed ? "ok" : "FAILED")}: {check.Name} - {check.Detail}");
if (lockoutGuard.IsTripped)
	Console.Error.WriteLine("ABORTED: Login lockout guard tripped on refused login burst.");
if (restarted)
	Console.Error.WriteLine("ABORTED: The server restarted during the run.");

return exitCode;

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
	                    plan <test>   Print the planned duration of a test in seconds

	                  Exit codes: 0 SLO met, 3 SLO broken, 1 the run itself broke (bad options, lockout guard, server restart, crash).

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
	                    (the seed owner password is read from the environment variable BASIL_OWNER_PASSWORD)
	                    --players <n>          Player accounts to seed (default: 1500)
	                    --referees <n>         Referee accounts to seed (default: 100)
	                    --tourneys <n>         Tourney accounts to seed (default: 400)
	                    --expected-sets <n>    Expected beatmapsets for seed wait (default: 15)
	                  """);
}