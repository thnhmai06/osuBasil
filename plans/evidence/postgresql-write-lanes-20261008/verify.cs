// Storage verification against a real PostgreSQL: repositories through DI, restart, writer internals (by reflection),
// backend terminations during a flood, shutdown. Usage: dotnet run verify.cs (needs the basil role with CREATEDB).
#:property PublishAot=false
#:property Nullable=enable
#:property ManagePackageVersionsCentrally=false
#:package Microsoft.Extensions.Hosting@10.0.0
#:project ../../../src/Infrastructure/Storage/Basil.Infrastructure.Storage.csproj
using System.Reflection;
using Basil.Application.Storage.Contracts.Beatmaps;
using Basil.Application.Storage.Contracts.Common;
using Basil.Application.Storage.Contracts.Content;
using Basil.Application.Storage.Contracts.Multiplayer;
using Basil.Application.Storage.Contracts.Scores;
using Basil.Application.Storage.Contracts.Users;
using Basil.Domain.Beatmaps;
using Basil.Domain.Content;
using Basil.Domain.Mechanics;
using Basil.Domain.Multiplayer;
using Basil.Domain.Scores;
using Basil.Domain.Users;
using Basil.Domain.Utilities;
using Basil.Infrastructure.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

const string Admin = "Host=localhost;Database=basil;Username=basil;Password=basil";
var db = "basil_verify_" + Guid.NewGuid().ToString("N");
var cs = $"Host=localhost;Database={db};Username=basil;Password=basil";
var dataDir = Path.Combine(Path.GetTempPath(), db);
var failures = 0;
void Check(bool ok, string what) { Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  {what}"); if (!ok) failures++; }

await Exec(Admin, $"create database {db}");
try
{
	// ---------- first run ----------
	int alphaId, betaId, matchId, setId, keptId, taikoId;
	{
		using var host = BuildHost();
		await host.StartAsync();
		var sp = host.Services;
		var users = sp.GetRequiredService<IUserRepository>();
		var alpha = await users.CreateAsync(new UserData { Name = "Alpha User", Country = Country.Vn, Permissions = Permissions.Player });
		var beta = await users.CreateAsync(new UserData { Name = "Beta", Country = Country.Us, Permissions = Permissions.Player });
		alphaId = alpha.Id; betaId = beta.Id;
		Check(alpha.Id >= 1 && beta.Id > alpha.Id, "users get ids from the identity column");
		Check(!await users.RenameAsync(beta, "alpha user"), "rename to a taken safe name returns false");
		Check(await users.RenameAsync(beta, "Gamma"), "rename to a free name returns true");
		beta.Value.Country = Country.Vn;
		await users.CreateOrUpdateAsync(beta);           // snapshot upsert with explicit id (BY DEFAULT identity)
		var byName = await users.GetByNameAsync("ALPHA_user");
		Check(byName?.Id == alpha.Id, "lookup by safe name ignores case and spaces");

		var restrictions = sp.GetRequiredService<IRestrictionRepository>();
		var r = await restrictions.CreateAsync(new RestrictionData { User = alpha, Permissions = Permissions.PlayerChat, StartsAt = DateTimeOffset.UtcNow });
		r.Value.EndsAt = DateTimeOffset.UtcNow.AddHours(1);
		await restrictions.CreateOrUpdateAsync(r);

		var matches = sp.GetRequiredService<IMatchRepository>();
		var match = await matches.CreateAsync(new MatchData { Name = "Verify Match", StartedAt = DateTimeOffset.UtcNow, EndedAt = null, Creator = alpha, IsPrivate = false });
		matchId = match.Id;
		match.Value.EndedAt = DateTimeOffset.UtcNow;
		await matches.CreateOrUpdateAsync(match);
		var events = sp.GetRequiredService<IMatchEventRepository>();
		for (var i = 0; i < 3; i++)
			await events.CreateAsync(new MatchEvent(match, MatchEventType.Closed, DateTimeOffset.UtcNow, alpha, null, $"e{i}"));

		var sets = sp.GetRequiredService<IBeatmapsetRepository>();
		var set = await sets.CreateAsync(new BeatmapsetData { Artist = "Camellia", Title = "Verify Song", Creator = "Mapper", CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow });
		setId = set.Id;
		Check(set.Id >= Beatmapset.LocalIdFloor, $"local beatmapset id comes from the sequence ({set.Id})");
		var maps = sp.GetRequiredService<IBeatmapRepository>();
		var kept = await maps.CreateAsync(Map(set, "11111111111111111111111111111111", GameMode.Standard, "Insane"));
		var taiko = await maps.CreateAsync(Map(set, "22222222222222222222222222222222", GameMode.Taiko, "Oni"));
		var dropped = await maps.CreateAsync(Map(set, "33333333333333333333333333333333", GameMode.Standard, "Easy"));
		keptId = kept.Id; taikoId = taiko.Id;
		await maps.RetainAsync(set, [kept, taiko]);

		var scores = sp.GetRequiredService<IScoreRepository>();
		var score = new ScoreData(alpha.Id, kept.Value.Hash, GameMode.Standard, GameMods.NoMod, new HitCounts(100, 1, 0, 0, 0, 0),
			123456, 200, Grade.N, true, false, DateTimeOffset.UtcNow) { Checksum = new Md5("44444444444444444444444444444444") };
		Check(await scores.CreateAsync(score) is not null, "score is stored");
		Check(await scores.CreateAsync(score) is null, "a duplicate checksum yields null");

		var settings = sp.GetRequiredService<ISettingsRepository>();
		var current = await settings.GetAsync();
		await settings.CreateOrUpdateAsync(current with { Motd = "hello postgres" });

		var nulRefused = false;
		try { _ = new MatchData { Name = "a\0b", StartedAt = DateTimeOffset.UtcNow, EndedAt = null }; } catch (ArgumentException) { nulRefused = true; }
		Check(nulRefused, "a match name with NUL is refused by the Domain");

		await host.StopAsync();                            // flush every lane
	}

	// ---------- second run: everything comes back from the database ----------
	{
		using var host = BuildHost();
		await host.StartAsync();
		var sp = host.Services;
		var users = sp.GetRequiredService<IUserRepository>();
		var beta = await users.GetAsync(betaId);
		Check(beta is { Value.Name: "Gamma", Value.Country: Country.Vn }, "rename and snapshot survived a restart");
		var vn = await users.ListAsync(new UserQuery(Countries: [Country.Vn]), new PageRequest(0, 10));
		Check(vn.Total == 2, $"country filter uses = any(...) ({vn.Total})");
		var alpha = (await users.GetAsync(alphaId))!;
		var rs = await sp.GetRequiredService<IRestrictionRepository>().ListAsync(alpha);
		Check(rs.Count == 1 && rs[0].Value.EndsAt is not null, "restriction and its later update stored");
		var match = await sp.GetRequiredService<IMatchRepository>().GetAsync(matchId);
		Check(match?.Value.EndedAt is not null, "match snapshot stored");
		var events = await sp.GetRequiredService<IMatchEventRepository>().ListAsync(match!);
		Check(events.Select(e => e.Detail).SequenceEqual(["e0", "e1", "e2"]), "match events appended in order");
		var set = (await sp.GetRequiredService<IBeatmapsetRepository>().GetAsync(setId))!;
		var maps = await sp.GetRequiredService<IBeatmapRepository>().ListAsync(set);
		Check(maps.Select(m => m.Id).Order().SequenceEqual(new[] { keptId, taikoId }.Order()), "RetainAsync deleted the others in one statement");
		Check(maps.Any(m => m.Id == taikoId && m.Value.Objects is TaikoObjects), "taiko objects read back from jsonb");
		var found = await sp.GetRequiredService<IBeatmapRepository>().ListAsync(new BeatmapQuery(Text: "CAMELLIA"), new PageRequest(0, 10));
		Check(found.Total == 2, $"beatmap text search ignores case (ILIKE) ({found.Total})");
		var circles = await sp.GetRequiredService<IBeatmapRepository>().ListAsync(new BeatmapQuery(Circles: new Interval<int>(10, 10)), new PageRequest(0, 10));
		Check(circles.Total == 1, $"circle count filter reads jsonb ({circles.Total})");
		var settings = await sp.GetRequiredService<ISettingsRepository>().GetAsync();
		Check(settings.Motd == "hello postgres", "settings update stored");

		// ---------- the writer itself (internal, through reflection) ----------
		var asm = typeof(StorageOptions).Assembly;
		var writerType = asm.GetType("Basil.Infrastructure.Storage.Writing.DatabaseWriter")!;
		var rootType = asm.GetType("Basil.Infrastructure.Storage.Writing.Root")!;
		var commandType = asm.GetType("Basil.Infrastructure.Storage.Writing.WriteCommand")!;
		var writer = sp.GetRequiredService(writerType);
		object Root(string kind, int id) => kind == "Server"
			? rootType.GetProperty("Server")!.GetValue(null)!
			: rootType.GetMethod(kind)!.Invoke(null, [id])!;
		Task Enqueue(object root, object identity, string sql, object parameters) =>
			(Task)writerType.GetMethod("EnqueueAsync")!.Invoke(writer, [root, identity, Activator.CreateInstance(commandType, sql, parameters)!])!;

		await Exec(cs, "create table probe (lane text, n integer, id bigint generated always as identity)");
		var good1 = Enqueue(Root("User", 1), new object(), "insert into probe (lane, n) values (@Lane, @N)", new { Lane = "bad-batch", N = 1 });
		var bad = Enqueue(Root("User", 1), new object(), "insert into missing_table values (@N)", new { N = 2 });
		var good2 = Enqueue(Root("User", 1), new object(), "insert into probe (lane, n) values (@Lane, @N)", new { Lane = "bad-batch", N = 3 });
		await Task.WhenAll(good1, good2);
		Check(bad.IsFaulted, "a statement the database refuses fails alone");
		Check(await Scalar<long>(cs, "select count(*) from probe where lane = 'bad-batch'") == 2, "the rest of the batch is stored");

		var tasks = new List<Task>();
		for (var n = 0; n < 500; n++)
		{
			tasks.Add(Enqueue(Root("Match", 1), new object(), "insert into probe (lane, n) values (@Lane, @N)", new { Lane = "a", N = n }));
			tasks.Add(Enqueue(Root("Match", 2), new object(), "insert into probe (lane, n) values (@Lane, @N)", new { Lane = "b", N = n }));
		}
		await Task.WhenAll(tasks);
		var ordered = await Scalar<bool>(cs, """
			select bool_and(ok) from (
			  select n = row_number() over (partition by lane order by id) - 1 as ok from probe where lane in ('a', 'b')) t
			""");
		Check(ordered, "two roots in different lanes interleave, each keeps its order");

		var xidCommitted = await Scalar<long>(cs, "begin; select pg_current_xact_id()::text::bigint;", commit: true);
		Check(await Scalar<string>(cs, $"select pg_xact_status('{xidCommitted}'::xid8)") == "committed", "pg_xact_status reports a committed transaction");

		// the database drops every connection three times during a flood: nothing is lost or stored twice
		var flood = new List<Task>();
		var restrictionsRepo = sp.GetRequiredService<IRestrictionRepository>();
		var tryOps = new List<Task>();
		for (var n = 0; n < 30_000; n++)
		{
			flood.Add(Enqueue(Root("Match", n % 40), new object(), "insert into probe (lane, n) values (@Lane, @N)", new { Lane = "kill", N = n }));
			if (n % 1500 == 0)
				tryOps.Add(restrictionsRepo.CreateAsync(new RestrictionData { User = alpha, Permissions = Permissions.PlayerSpectate, StartsAt = DateTimeOffset.UtcNow }));
			if (n is 5_000 or 15_000 or 25_000)
			{
				var killed = await Scalar<long>(Admin.Replace("Database=basil", "Database=postgres"),
					$"select count(pg_terminate_backend(pid)) from pg_stat_activity where datname = '{db}' and pid <> pg_backend_pid()");
				Console.WriteLine($"      terminated {killed} backends at write {n}");
				await Task.Delay(30);
			}
		}

		await Task.WhenAll(flood.Concat(tryOps));
		Check(await Scalar<long>(cs, "select count(*) from probe where lane = 'kill'") == 30_000,
			"after three terminations every write of the flood is stored");
		Check(await Scalar<long>(cs, "select count(distinct n) from probe where lane = 'kill'") == 30_000,
			"and none is stored twice");
		Check(await Scalar<long>(cs, $"select count(*) from restrictions where permissions = {(long)Permissions.PlayerSpectate}") == tryOps.Count,
			$"each of the {tryOps.Count} racing try-operations is stored exactly once");

		// queued writes at shutdown are all stored
		for (var n = 0; n < 300; n++)
			_ = Enqueue(Root("User", n), new object(), "insert into probe (lane, n) values (@Lane, @N)", new { Lane = "shutdown", N = n });
		await host.StopAsync();
		Check(await Scalar<long>(cs, "select count(*) from probe where lane = 'shutdown'") == 300, "shutdown stores every queued write");
	}
}
finally
{
	NpgsqlConnection.ClearAllPools();
	await Exec(Admin, $"drop database {db} with (force)");
	if (Directory.Exists(dataDir)) Directory.Delete(dataDir, true);
}

Console.WriteLine(failures == 0 ? "ALL PASS" : $"{failures} FAILED");
return failures;

IHost BuildHost()
{
	var builder = Host.CreateApplicationBuilder();
	builder.Logging.ClearProviders().AddSimpleConsole().SetMinimumLevel(LogLevel.Warning);
	builder.Services.AddSingleton(Microsoft.Extensions.Options.Options.Create(new StorageOptions { DataDirectory = dataDir, ConnectionString = cs }));
	builder.Services.AddInfrastructureStorage();
	return builder.Build();
}

static BeatmapData Map(Beatmapset set, string hash, GameMode mode, string version)
{
	var objects = BeatmapObjects.NewFrom(mode);
	if (objects is OsuObjects osu) { osu.Circles = version == "Insane" ? 10 : 3; osu.Sliders = 2; }
	if (objects is TaikoObjects taiko) taiko.Hits = 7;
	objects.Total = 12; objects.MaxCombo = 20;
	return new BeatmapData
	{
		Hash = new Md5(hash), Beatmapset = set, Version = version,
		Difficulty = new Difficulty(mode, 180, TimeSpan.FromSeconds(90), 4, 9, 8, 5, 5.5), Objects = objects
	};
}

static async Task Exec(string connection, string sql)
{
	await using var c = new NpgsqlConnection(connection + ";Pooling=false");
	await c.OpenAsync();
	await using var cmd = new NpgsqlCommand(sql, c);
	await cmd.ExecuteNonQueryAsync();
}

static async Task<T> Scalar<T>(string connection, string sql, bool commit = false)
{
	await using var c = new NpgsqlConnection(connection + ";Pooling=false");
	await c.OpenAsync();
	await using var cmd = new NpgsqlCommand(commit ? sql + " commit;" : sql, c);
	var value = await cmd.ExecuteScalarAsync();
	return (T)Convert.ChangeType(value!, typeof(T));
}
