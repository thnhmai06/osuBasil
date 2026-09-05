using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Basil.LoadTests.Analysis;
using Basil.LoadTests.Client;
using Basil.LoadTests.Configuration;
using Basil.LoadTests.Helpers;
using Basil.LoadTests.Hosting;
using Basil.LoadTests.Infrastructure.Metrics;
using Basil.LoadTests.Infrastructure.Reporting;
using Basil.LoadTests.Scenarios;
using Microsoft.Extensions.Configuration;
using NBomber.Contracts;
using NBomber.CSharp;

var profileName = GetArg(args, "--profile") ?? "quick";
var scenarioFilter = GetArg(args, "--scenario");

var profilePath = Path.Combine(AppContext.BaseDirectory, "Profiles", $"{profileName}.json");
if (!File.Exists(profilePath))
{
	Console.Error.WriteLine($"Profile not found: {profilePath}");
	return 1;
}

LogInfo($"Loading profile '{profileName}' from {profilePath}");

var configuration = new ConfigurationBuilder()
	.AddJsonFile(profilePath, false)
	.Build();

var profile = configuration.Get<LoadProfile>() ??
              throw new InvalidOperationException($"Failed to bind profile '{profileName}'.");

var reportFolder = Path.Combine(RepoPaths.Resolve(profile.Report.Folder),
	$"{profile.Name}-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}");
Directory.CreateDirectory(reportFolder);
LogInfo($"Report folder: {reportFolder}");

await using var host = ServerHostFactory.Create(profile.ServerHost, profile.Metrics.DotnetCounters, LogWarning);

var manifest = new RunManifest
{
	ProfileName = profile.Name,
	StartedUtc = DateTimeOffset.UtcNow,
	OsDescription = RuntimeInformation.OSDescription,
	OsArchitecture = RuntimeInformation.OSArchitecture.ToString(),
	FrameworkDescription = RuntimeInformation.FrameworkDescription,
	GitCommit = TryGetGitCommit(),
	Profile = profile,
	Capabilities = host.Capabilities
};

// Phase 1: startup/idle-resource benchmark. Not an NBomber scenario — it owns the server's
// lifecycle itself (repeated start/settle/stop), so it runs before the server is started for
// the main scenario run below.
var startupSettings = configuration.GetSection("Scenarios:startup").Get<StartupSettings>() ?? new StartupSettings();
if (startupSettings.Enabled)
{
	LogInfo($"Running startup benchmark: {startupSettings.Iterations} iteration(s)...");
	var startupResult = await StartupBenchmark.RunAsync(host, startupSettings, profile.ServerHost.IdleSettle,
		profile.Metrics.SampleInterval);
	await WriteStartupReportAsync(reportFolder, startupResult);
	LogInfo("Startup benchmark complete.");
}

LogInfo("Starting server for the main run...");
await host.StartAsync();
manifest.StartupTime = await host.WaitUntilHealthyAsync();
LogInfo(manifest.StartupTime.HasValue
	? $"Server healthy after {TimeSpanFormat.Humanize(manifest.StartupTime.Value)}."
	: "Server healthy (startup time not available on this host).");

var accounts = AccountSeeder.BuildAccounts(profile.Accounts);
var snapshotPath = Path.Combine(RepoPaths.Resolve(".loadtest/snapshots"),
	$"basil-{profile.Accounts.NamePrefix}-{profile.Accounts.Count}-{Md5Hex.Of(profile.Accounts.Password)[..8]}.db");

if (host.Capabilities.CanSnapshotDatabase)
{
	await host.StopAsync();
	var restored = await host.SyncDatabaseSnapshotAsync(snapshotPath, true);
	await host.StartAsync();
	await host.WaitUntilHealthyAsync();

	if (restored)
	{
		LogInfo($"Restored account snapshot from {snapshotPath}; skipping seeding.");
	}
	else
	{
		LogInfo($"No snapshot found; seeding {accounts.Count} account(s) (this pays bcrypt once)...");
		int seedFailures;
		using (var seedClientFactory = new BasilHttpClientFactory(host.Endpoint, profile.Client))
		{
			seedFailures = await new AccountSeeder(new BasilApiClient(seedClientFactory))
				.SeedAsync(accounts, LogWarning);
		}

		if (seedFailures > 0) LogWarning($"{seedFailures} account(s) failed to seed after a retry.");

		await host.StopAsync();
		await host.SyncDatabaseSnapshotAsync(snapshotPath, false);
		await host.StartAsync();
		await host.WaitUntilHealthyAsync();
		LogInfo($"Seeding complete; snapshot captured to {snapshotPath}.");
	}
}
else
{
	manifest.Notes.Add("This host cannot snapshot the database; accounts were ensured to exist " +
	                   "(tolerant of pre-existing accounts from a prior run) rather than restored from a snapshot.");
	LogInfo($"Host cannot snapshot the database; ensuring {accounts.Count} account(s) exist...");
	using var seedClientFactory = new BasilHttpClientFactory(host.Endpoint, profile.Client);
	var seedFailures = await new AccountSeeder(new BasilApiClient(seedClientFactory))
		.SeedAsync(accounts, LogWarning);
	if (seedFailures > 0) LogWarning($"{seedFailures} account(s) failed to seed after a retry.");
}

using var clientFactory = new BasilHttpClientFactory(host.Endpoint, profile.Client);

var loginSettings = configuration.GetSection("Scenarios:login").Get<LoginSettings>() ?? new LoginSettings();
if (loginSettings.Enabled)
	// Disclosed unconditionally (not just when it's false) — a reader must never have to infer this
	// from the profile file. Warm measures steady-state login cost; cold measures the tournament-wave
	// worst case, understated by every warm figure in this project's history until this note existed.
	manifest.Notes.Add(loginSettings.WarmBcryptCache
		? "Login scenario ran with the bcrypt-verify cache pre-warmed: reported login latency is the " +
		  "steady-state cost, not first-login (cold cache) cost."
		: "Login scenario ran with the bcrypt-verify cache cold (WarmBcryptCache=false): reported login " +
		  "latency for the first concurrency level includes full bcrypt verify cost per login. Only the " +
		  "first concurrency level in the run is genuinely cold — later levels reuse accounts already " +
		  "warmed by it, since account slices overlap (Take(n) from a shared pool).");

if (loginSettings is { Enabled: true, WarmBcryptCache: true })
{
	LogInfo("Warming the bcrypt-verify cache...");
	await LoginScenario.WarmBcryptCacheAsync(accounts, clientFactory, LogInfo);

	// The warm-up leaves every account with a live session (its logout would fall inside the server's
	// 1s grace and be ignored). The server rejects a relogin within 10s of the session's last poll,
	// so settle past that guard before the scenario starts, or every account's first login fails.
	if (loginSettings.PostWarmupSettle > TimeSpan.Zero)
	{
		LogInfo(
			$"Settling {loginSettings.PostWarmupSettleSeconds:F0}s so warm-up sessions age past the relogin guard...");
		await Task.Delay(loginSettings.PostWarmupSettle);
	}
}

var scenarioContext = new BasilScenarioContext
{
	Profile = profile,
	Configuration = configuration,
	Accounts = accounts,
	ClientFactory = clientFactory,
	Host = host,
	LogInfo = LogInfo,
	LogWarning = LogWarning,
	ReportFolder = reportFolder
};

var timeline = new ResourceTimeline();
using var samplingCts = new CancellationTokenSource();
var samplingTask = SampleResourcesLoopAsync(host, timeline, profile.Metrics.SampleInterval, samplingCts.Token);

foreach (var scenario in ScenarioCatalog.All)
{
	if (scenarioFilter is not null && !string.Equals(scenario.Id, scenarioFilter, StringComparison.OrdinalIgnoreCase))
		continue;

	List<ScenarioProps> propsList;
	try
	{
		propsList = [.. scenario.Build(scenarioContext)];
	}
	catch (Exception ex)
	{
		LogWarning($"Scenario '{scenario.Id}' failed to build: {ex.Message}");
		continue;
	}

	if (propsList.Count == 0)
	{
		LogInfo($"Scenario '{scenario.Id}' is disabled or produced no variants; skipping.");
		continue;
	}

	// A dead server otherwise goes unnoticed until every remaining scenario finishes reporting
	// 100% failures — including a multi-hour soak run against nothing. See the 2026-08-25/26 Phase 0
	// baseline, where the server crashed mid-run and the harness spent the next three hours hammering
	// a closed port before anyone noticed.
	try
	{
		await ServerReadinessProbe.WaitAsync(clientFactory, TimeSpan.FromSeconds(10));
	}
	catch (TimeoutException)
	{
		LogWarning($"Server is not responding before scenario '{scenario.Id}' — aborting the remaining " +
		           "run instead of measuring against a dead target.");
		break;
	}

	for (var i = 0; i < propsList.Count; i++)
	{
		LogInfo($"Running scenario '{scenario.Id}' ({i + 1}/{propsList.Count})...");
		var scenarioReportFolder = Path.Combine(reportFolder, scenario.Id, (i + 1).ToString());
		Directory.CreateDirectory(scenarioReportFolder);

		var runner = NBomberRunner
			.RegisterScenarios(propsList[i])
			.WithTestSuite("basil-load-tests")
			.WithTestName($"{profile.Name}-{scenario.Id}-{i + 1}")
			.WithReportFolder(scenarioReportFolder)
			.WithReportFormats(NBomberReportOptions.Parse(profile.Report.Formats))
			.DisplayConsoleMetrics(false);

		// A multi-hour soak needs interim stats, not just a report at the very end.
		if (scenario.Id == "soak")
		{
			var soakSettings = configuration.GetSection("Scenarios:soak").Get<SoakSettings>() ?? new SoakSettings();
			runner = runner.WithReportingInterval(soakSettings.ReportingInterval);
		}

		runner.Run();
	}
}

if (configuration.GetSection("Scenarios:soak").Get<SoakSettings>() is { Enabled: true } enabledSoakSettings)
{
	LogInfo("Running soak leak analysis...");
	var verdicts = SoakAnalyzer.Analyze(timeline, enabledSoakSettings.WarmUp, enabledSoakSettings.Duration,
		enabledSoakSettings.LeakSlopeThresholds);
	await SoakAnalyzer.WriteReportAsync(reportFolder, verdicts);
}

await samplingCts.CancelAsync();
try
{
	await samplingTask;
}
catch (OperationCanceledException)
{
	// expected: the sampling loop observes the cancellation and stops.
}

await host.StopAsync();
await host.ExportResultsAsync(reportFolder);

manifest.FinishedUtc = DateTimeOffset.UtcNow;
await ReportWriter.WriteRunJsonAsync(reportFolder, manifest);
ReportWriter.WriteResourcesCsv(reportFolder, timeline);
await ReportWriter.WriteSummaryMarkdownAsync(reportFolder, manifest, timeline);

LogInfo($"Run complete. Reports written to {reportFolder}");
return 0;

void LogInfo(string message)
{
	Console.WriteLine($"[{DateTimeOffset.Now:HH:mm:ss}] {message}");
}

void LogWarning(string message)
{
	Console.WriteLine($"[{DateTimeOffset.Now:HH:mm:ss}] WARN: {message}");
}

static string? GetArg(string[] commandLineArgs, string name)
{
	var index = Array.IndexOf(commandLineArgs, name);
	return index >= 0 && index + 1 < commandLineArgs.Length ? commandLineArgs[index + 1] : null;
}

static string? TryGetGitCommit()
{
	try
	{
		using var process = Process.Start(new ProcessStartInfo("git", "rev-parse --short HEAD")
		{
			WorkingDirectory = RepoPaths.RepoRoot,
			RedirectStandardOutput = true,
			UseShellExecute = false
		});
		if (process is null) return null;

		var output = process.StandardOutput.ReadToEnd().Trim();
		process.WaitForExit(2000);
		return process.ExitCode == 0 && output.Length > 0 ? output : null;
	}
	catch (Win32Exception)
	{
		return null;
	}
}

static async Task SampleResourcesLoopAsync(IServerHost host, ResourceTimeline timeline, TimeSpan interval,
	CancellationToken cancellationToken)
{
	try
	{
		while (!cancellationToken.IsCancellationRequested)
		{
			timeline.Add(await host.CollectMetricsAsync(cancellationToken));
			await Task.Delay(interval, cancellationToken);
		}
	}
	catch (OperationCanceledException)
	{
		// expected on shutdown
	}
}

static async Task WriteStartupReportAsync(string reportFolder, StartupBenchmarkResult result)
{
	var lines = new List<string> { "# Startup benchmark", "" };

	if (result.StartupTimes.Count > 0)
	{
		var sorted = result.StartupTimes.OrderBy(t => t).ToList();
		lines.Add($"- Iterations measured: {sorted.Count}");
		lines.Add($"- Min startup time: {TimeSpanFormat.Humanize(sorted[0])}");
		lines.Add($"- Median startup time: {TimeSpanFormat.Humanize(sorted[sorted.Count / 2])}");
		lines.Add($"- Max startup time: {TimeSpanFormat.Humanize(sorted[^1])}");
	}
	else
	{
		lines.Add("- Startup time: not available on this host.");
	}

	lines.Add("");

	if (result.IdleSamples.Count > 0)
	{
		var idleTimeline = new ResourceTimeline();
		foreach (var sample in result.IdleSamples) idleTimeline.Add(sample);
		idleTimeline.WriteCsv(Path.Combine(reportFolder, "startup-idle-samples.csv"));

		var aggregates = idleTimeline.Aggregate();
		lines.Add("## Idle resource usage");
		lines.Add("| Metric | Min | Mean | Max |");
		lines.Add("|---|---|---|---|");
		AddIdleRow(lines, aggregates, "CpuPercent", "CPU %");
		AddIdleRow(lines, aggregates, "WorkingSetBytes", "Working set (MB)", 1.0 / (1024 * 1024), 1);
		AddIdleRow(lines, aggregates, "PrivateMemoryBytes", "Private memory (MB)", 1.0 / (1024 * 1024), 1);
		AddIdleRow(lines, aggregates, "GcHeapBytes", "GC heap (MB)", 1.0 / (1024 * 1024), 1);
		AddIdleRow(lines, aggregates, "ThreadCount", "Thread count", 1, 0);
		AddIdleRow(lines, aggregates, "HandleCount", "Handle count", 1, 0);
	}

	await File.WriteAllLinesAsync(Path.Combine(reportFolder, "startup-benchmark.md"), lines);
}

static void AddIdleRow(List<string> lines, IReadOnlyDictionary<string, FieldAggregate> aggregates, string key,
	string label, double scale = 1.0, int decimals = 2)
{
	var aggregate = aggregates.TryGetValue(key, out var value) ? value : new FieldAggregate(null, null, null);

	lines.Add($"| {label} | {Format(aggregate.Min)} | {Format(aggregate.Mean)} | {Format(aggregate.Max)} |");
	return;

	string Format(double? v)
	{
		return v.HasValue ? (v.Value * scale).ToString($"F{decimals}") : "n/a";
	}
}