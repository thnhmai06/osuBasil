#:property PublishAot=false
#:property Nullable=enable
#:package Microsoft.Extensions.DependencyInjection
#:project ../../../src/Infrastructure/Storage/Basil.Infrastructure.Storage.csproj

// Benchmark behind the choice of DatabaseOptions.WriteLanes and of the pg_trgm index and fillfactor in
// 001_baseline.sql. See results.md next to this file for the method and the numbers.
//
// Usage: dotnet run bench.cs -- "<admin connection string with CREATEDB>" [lanes=1,2,4,8] [runs=3]

using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Basil.Application.Storage.Contracts.Multiplayer;
using Basil.Application.Storage.Contracts.Scores;
using Basil.Application.Storage.Contracts.Users;
using Basil.Domain.Auth;
using Basil.Domain.Mechanics;
using Basil.Domain.Multiplayer;
using Basil.Domain.Scores;
using Basil.Domain.Users;
using Basil.Infrastructure.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;

var admin = args.Length > 0 ? args[0] : "Host=localhost;Database=basil;Username=basil;Password=basil";
var lanesToTry = (args.Length > 1 ? args[1] : "1,2,4,8,12,16").Split(',').Select(int.Parse).ToArray();
var runs = args.Length > 2 ? int.Parse(args[2]) : 3;

const int Users = 1000, Matches = 16, EventsPerMatch = 2500, LoginsPerUser = 20;
const int FloodWrites = Matches * EventsPerMatch + Users * LoginsPerUser + Users; // events + logins + stats
const int SteadyRate = 10_000, SteadySeconds = 4, ProbeEveryMs = 20;

await PrintEnvironmentAsync();

// ---------------- write lanes ----------------
Console.WriteLine("## Write lanes");
Console.WriteLine();
Console.WriteLine($"Each run, on a fresh database with {Users} users and {Matches} matches:");
Console.WriteLine();
Console.WriteLine($"1. Steady load: {SteadyRate} appends/s (logins and match events, every one a new row) for {SteadySeconds} s, " +
                  $"with a try-operation (restriction insert) every {ProbeEveryMs} ms; the try-operation latency is measured.");
Console.WriteLine($"2. Capacity: {FloodWrites} writes queued at once ({Matches * EventsPerMatch} match events, {Users * LoginsPerUser} logins, " +
                  $"{Users} user stats of distinct users), timed until every row is in the database.");
Console.WriteLine();
Console.WriteLine("| lanes | run | flood drain s | committed writes/s | try p50 ms | try p99 ms | try max ms |");
Console.WriteLine("|---|---|---|---|---|---|---|");
var summary = new List<(int Lanes, double Writes, double P50, double P99, double Max)>();
foreach (var lanes in lanesToTry)
{
	var results = new List<RunResult>();
	for (var run = 1; run <= runs; run++)
	{
		var r = await RunLanesAsync(lanes);
		results.Add(r);
		Console.WriteLine($"| {lanes} | {run} | {r.Drain:F2} | {r.Writes:F0} | {r.P50:F1} | {r.P99:F1} | {r.Max:F1} |");
	}

	summary.Add((lanes, Median(results.Select(r => r.Writes)),
		Median(results.Select(r => r.P50)), Median(results.Select(r => r.P99)), Median(results.Select(r => r.Max))));
}

Console.WriteLine();
Console.WriteLine("Medians:");
Console.WriteLine();
Console.WriteLine("| lanes | committed writes/s | try p50 ms | try p99 ms | try max ms |");
Console.WriteLine("|---|---|---|---|---|");
foreach (var s in summary)
	Console.WriteLine($"| {s.Lanes} | {s.Writes:F0} | {s.P50:F1} | {s.P99:F1} | {s.Max:F1} |");

var bestWrites = summary.Max(s => s.Writes);
var bestP99 = summary.Min(s => s.P99);
var chosen = summary.Where(s => s.Writes >= bestWrites * 0.9 && s.P99 <= bestP99 * 1.1 + 1).Select(s => s.Lanes).DefaultIfEmpty(-1).Min();
Console.WriteLine();
Console.WriteLine($"Rule: smallest lane count within 10% of the best capacity ({bestWrites:F0} committed writes/s) and with a steady-load p99 " +
                  $"no worse than 1.1 × {bestP99:F1} ms + 1 ms: {(chosen < 0 ? "none" : chosen)}");
Console.WriteLine();

// ---------------- pg_trgm ----------------
Console.WriteLine("## pg_trgm index on users.safe_name (partial-name search, median of 7, ms)");
Console.WriteLine();
Console.WriteLine("| users | with index | without index |");
Console.WriteLine("|---|---|---|");
foreach (var count in new[] { 1_000, 100_000 })
{
	var (with, without) = await TrigramAsync(count);
	Console.WriteLine($"| {count} | {with:F3} | {without:F3} |");
}

Console.WriteLine();

// ---------------- fillfactor ----------------
Console.WriteLine("## fillfactor on frequently updated tables (HOT share of updates)");
Console.WriteLine();
Console.WriteLine("| table | fillfactor 90 | fillfactor 100 |");
Console.WriteLine("|---|---|---|");
var hot90 = await HotShareAsync(90);
var hot100 = await HotShareAsync(100);
foreach (var table in hot90.Keys)
	Console.WriteLine($"| {table} | {hot90[table]:P1} | {hot100[table]:P1} |");
return;

async Task<RunResult> RunLanesAsync(int lanes)
{
	var db = "basil_bench_" + Guid.NewGuid().ToString("N");
	await ExecAsync(admin, $"create database {db}");
	var connection = new NpgsqlConnectionStringBuilder(admin) { Database = db }.ConnectionString;
	var dataDirectory = Path.Combine(Path.GetTempPath(), db);
	try
	{
		await using var provider = Services(connection, dataDirectory, lanes);
		var hosted = provider.GetServices<IHostedService>().ToList();
		foreach (var service in hosted) await service.StartAsync(CancellationToken.None);

		await ExecAsync(connection, $"""
			insert into users (name, country, permissions) select 'player ' || g, 1, 0 from generate_series(1, {Users}) g;
			insert into matches (name, creator_id, started_at, is_private) select 'match ' || g, g, now(), false from generate_series(1, {Matches}) g;
			""");
		var users = provider.GetRequiredService<IUserRepository>();
		var matches = provider.GetRequiredService<IMatchRepository>();
		var events = provider.GetRequiredService<IMatchEventRepository>();
		var logins = provider.GetRequiredService<ILoginRepository>();
		var stats = provider.GetRequiredService<IUserStatsRepository>();
		var restrictions = provider.GetRequiredService<IRestrictionRepository>();
		var userList = new List<User>();
		for (var id = 1; id <= Users; id++) userList.Add((await users.GetAsync(id))!);
		var matchList = new List<Match>();
		for (var id = 1; id <= Matches; id++) matchList.Add((await matches.GetAsync(id))!);

		// 1. Steady load with try-operations.
		var latencies = new List<double>();
		var steady = Task.Run(async () =>
		{
			var clock = Stopwatch.StartNew();
			long sent = 0, total = (long)SteadyRate * SteadySeconds;
			while (sent < total)
			{
				var due = Math.Min(total, (long)(clock.Elapsed.TotalSeconds * SteadyRate));
				for (; sent < due; sent++)
					if (sent % 3 == 0)
						await logins.CreateAsync(new Login { User = userList[(int)(sent % Users)], Ip = IPAddress.Loopback, Timestamp = DateTimeOffset.UtcNow });
					else
						await events.CreateAsync(new MatchEvent(matchList[(int)(sent % Matches)], MatchEventType.Closed, DateTimeOffset.UtcNow, null, null, "steady"));
				await Task.Delay(1);
			}
		});
		var probe = Task.Run(async () =>
		{
			var random = new Random(42);
			var clock = Stopwatch.StartNew();
			while (clock.Elapsed.TotalSeconds < SteadySeconds)
			{
				var user = userList[random.Next(userList.Count)];
				var started = Stopwatch.GetTimestamp();
				await restrictions.CreateAsync(new RestrictionData { User = user, Permissions = Permissions.PlayerChat, StartsAt = DateTimeOffset.UtcNow });
				latencies.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
				await Task.Delay(ProbeEveryMs);
			}
		});
		await Task.WhenAll(steady, probe);
		var expected = (long)SteadyRate * SteadySeconds;
		await WaitForRowsAsync(connection, expected);

		// 2. Capacity: everything queued at once.
		var flood = Stopwatch.StartNew();
		var producers = new List<Task>();
		foreach (var match in matchList)
			producers.Add(Task.Run(async () =>
			{
				for (var i = 0; i < EventsPerMatch; i++)
					await events.CreateAsync(new MatchEvent(match, MatchEventType.Closed, DateTimeOffset.UtcNow, null, null, "flood"));
			}));
		foreach (var chunk in userList.Chunk(125))
			producers.Add(Task.Run(async () =>
			{
				foreach (var user in chunk)
				{
					for (var i = 0; i < LoginsPerUser; i++)
						await logins.CreateAsync(new Login { User = user, Ip = IPAddress.Loopback, Timestamp = DateTimeOffset.UtcNow });
					await stats.CreateOrUpdateAsync(new UserStats { UserId = user.Id, Mode = GameMode.Standard, TotalScore = 1, RankedScore = 1, PlayCount = 1 });
				}
			}));
		await Task.WhenAll(producers);
		await WaitForRowsAsync(connection, expected + FloodWrites);
		var drain = flood.Elapsed.TotalSeconds;

		foreach (var service in hosted.AsEnumerable().Reverse()) await service.StopAsync(CancellationToken.None);
		foreach (var service in hosted.OfType<IHostedLifecycleService>()) await service.StoppedAsync(CancellationToken.None);

		latencies.Sort();
		return new RunResult(drain, FloodWrites / drain,
			Percentile(latencies, 0.50), Percentile(latencies, 0.99), latencies.Count == 0 ? double.NaN : latencies[^1]);
	}
	finally
	{
		NpgsqlConnection.ClearAllPools();
		await ExecAsync(admin, $"drop database {db} with (force)");
		if (Directory.Exists(dataDirectory)) Directory.Delete(dataDirectory, true);
	}
}

static async Task WaitForRowsAsync(string connection, long expected)
{
	while (await ScalarAsync<long>(connection,
		       "select (select count(*) from match_events) + (select count(*) from logins) + (select count(*) from user_stats)") < expected)
		await Task.Delay(5);
}

async Task<(double With, double Without)> TrigramAsync(int count)
{
	return await WithSchemaAsync(async connection =>
	{
		await ExecAsync(connection, $"insert into users (name, country, permissions) select 'player ' || md5(g::text), 1, 0 from generate_series(1, {count}) g; analyze users;");
		var with = await QueryTimeAsync(connection);
		await ExecAsync(connection, "drop index users_safe_name_trgm; analyze users;");
		var without = await QueryTimeAsync(connection);
		return (with, without);
	});

	static async Task<double> QueryTimeAsync(string connection)
	{
		var times = new List<double>();
		for (var i = 0; i < 7; i++)
		{
			var plan = await ScalarAsync<string>(connection,
				"explain (analyze, format json) select id from users where safe_name like '%' || replace(lower('AB12'), ' ', '_') || '%'");
			times.Add(JsonDocument.Parse(plan).RootElement[0].GetProperty("Execution Time").GetDouble());
		}

		return Median(times);
	}
}

async Task<Dictionary<string, double>> HotShareAsync(int fillfactor)
{
	return await WithSchemaAsync(async connection =>
	{
		await ExecAsync(connection, $"""
			alter table users set (fillfactor = {fillfactor});
			alter table user_stats set (fillfactor = {fillfactor});
			insert into users (name, country, permissions) select 'player ' || g, 1, 0 from generate_series(1, {Users}) g;
			insert into user_stats (user_id, mode, total_score, ranked_score, play_count) select id, 0, 0, 0, 0 from users;
			""");
		// The writer's pattern: batches that upsert the snapshot of some identities, many times over.
		var random = new Random(7);
		for (var batch = 0; batch < 200; batch++)
		{
			var ids = string.Join(',', Enumerable.Range(0, 50).Select(_ => random.Next(1, Users + 1)).Distinct());
			await ExecAsync(connection, $"""
				insert into users (id, name, country, permissions) select id, name, country, permissions + 1 from users where id in ({ids})
				on conflict (id) do update set country = excluded.country, permissions = excluded.permissions;
				insert into user_stats (user_id, mode, total_score, ranked_score, play_count)
				select user_id, mode, total_score + 1, ranked_score, play_count + 1 from user_stats where user_id in ({ids})
				on conflict (user_id, mode) do update set total_score = excluded.total_score, play_count = excluded.play_count;
				""");
		}

		// Backends publish their statistics when they exit; close the pooled ones first.
		NpgsqlConnection.ClearAllPools();
		await Task.Delay(1100);
		var result = new Dictionary<string, double>();
		foreach (var table in new[] { "users", "user_stats" })
			result[table] = await ScalarAsync<double>(connection,
				$"select n_tup_hot_upd::float8 / nullif(n_tup_upd, 0) from pg_stat_user_tables where relname = '{table}'");
		return result;
	});
}

async Task<T> WithSchemaAsync<T>(Func<string, Task<T>> body)
{
	var db = "basil_bench_" + Guid.NewGuid().ToString("N");
	await ExecAsync(admin, $"create database {db}");
	var connection = new NpgsqlConnectionStringBuilder(admin) { Database = db }.ConnectionString;
	var dataDirectory = Path.Combine(Path.GetTempPath(), db);
	try
	{
		await using (var provider = Services(connection, dataDirectory, 1))
		{
			var startup = provider.GetServices<IHostedService>().First();
			await startup.StartAsync(CancellationToken.None); // StorageStartup: migrate only
		}

		return await body(connection);
	}
	finally
	{
		NpgsqlConnection.ClearAllPools();
		await ExecAsync(admin, $"drop database {db} with (force)");
		if (Directory.Exists(dataDirectory)) Directory.Delete(dataDirectory, true);
	}
}

ServiceProvider Services(string connection, string dataDirectory, int lanes)
{
	var services = new ServiceCollection();
	services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
	services.AddSingleton(Options.Create(new StorageOptions { DataDirectory = dataDirectory }));
	services.AddSingleton(Options.Create(new DatabaseOptions { WriteLanes = lanes, ConnectionString = connection }));
	services.AddInfrastructureStorage();
	return services.BuildServiceProvider();
}

async Task PrintEnvironmentAsync()
{
	Console.WriteLine("## Environment");
	Console.WriteLine();
	Console.WriteLine($"- .NET {Environment.Version}, {Environment.ProcessorCount} logical processors, {Environment.OSVersion}");
	Console.WriteLine($"- {await ScalarAsync<string>(admin, "select version()")}");
	foreach (var setting in new[] { "shared_buffers", "synchronous_commit", "wal_writer_delay", "max_connections", "fsync", "wal_level" })
		Console.WriteLine($"- {setting} = {await ScalarAsync<string>(admin, $"show {setting}")}");
	Console.WriteLine();
}

static async Task ExecAsync(string connection, string sql)
{
	await using var c = new NpgsqlConnection(connection);
	await c.OpenAsync();
	await using var command = new NpgsqlCommand(sql, c);
	await command.ExecuteNonQueryAsync();
}

static async Task<T> ScalarAsync<T>(string connection, string sql)
{
	await using var c = new NpgsqlConnection(connection);
	await c.OpenAsync();
	await using var command = new NpgsqlCommand(sql, c);
	return (T)Convert.ChangeType((await command.ExecuteScalarAsync())!, typeof(T));
}

static double Median(IEnumerable<double> values)
{
	var sorted = values.Order().ToArray();
	return sorted.Length == 0 ? double.NaN : sorted.Length % 2 == 1 ? sorted[sorted.Length / 2] : (sorted[sorted.Length / 2 - 1] + sorted[sorted.Length / 2]) / 2;
}

static double Percentile(List<double> sorted, double p) =>
	sorted.Count == 0 ? double.NaN : sorted[Math.Min(sorted.Count - 1, (int)Math.Ceiling(p * sorted.Count) - 1)];

record RunResult(double Drain, double Writes, double P50, double P99, double Max);
