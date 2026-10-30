using System.Globalization;
using System.Text;
using System.Text.Json;
using Basil.Application.Anticheat.Runtime;
using Basil.Application.Beatmaps.Capabilities;
using Basil.Application.Beatmaps.Persistence;
using Basil.Application.Common;
using Basil.Application.Common.Common;
using Basil.Application.Content.Persistence;
using Basil.Application.Scores.Persistence;
using Basil.Application.Scores.Runtime;
using Basil.Application.Servers.Runtime;
using Basil.Application.Users.Connections;
using Basil.Application.Users.Persistence;
using Basil.Application.Users.Users;
using Basil.Domain.Beatmaps;
using Basil.Domain.Client;
using Basil.Domain.Common;
using Basil.Domain.Mechanics;
using Basil.Domain.Scores;
using Basil.Domain.Users;
using Basil.Host.Bancho.Support;
using Basil.Protocol.Bancho.Models.Auth;
using Basil.Protocol.Bancho.Models.Scores;
using Basil.Protocol.Bancho.Wire.Security;
using Microsoft.AspNetCore.Mvc;

namespace Basil.Host.Bancho.Web;

/// <summary>The web endpoints of the osu! client (<c>osu.</c>), kept as the client expects them.</summary>
internal static class OsuWebRoutes
{
	/// <summary>Maps the web endpoints.</summary>
	/// <param name="app">The route builder of the <c>osu.</c> host.</param>
	public static void MapOsuWeb(this IEndpointRouteBuilder app)
	{
		app.MapGet("/", () => "osu");
		app.MapGet("/web/osu-osz2-getscores.php", GetScoresAsync);
		app.MapGet("/web/osu-getfriends.php", GetFriendsAsync);
		app.MapPost("/web/osu-getbeatmapinfo.php", GetBeatmapInfoAsync);
		app.MapPost("/web/osu-submit-modular-selector.php", SubmitAsync);
		app.MapGet("/web/osu-getreplay.php", GetReplayAsync);
		app.MapPost("/web/osu-screenshot.php", ScreenshotAsync);
		app.MapGet("/web/lastfm.php", LastFmAsync);
		app.MapGet("/web/osu-search.php", SearchAsync);
		app.MapGet("/web/osu-search-set.php", SearchSetAsync);
		app.MapGet("/d/{setId}", DownloadAsync);
		app.MapPost("/users", RegisterAsync);
		foreach (var stub in new[] { "bancho_connect.php", "check-updates.php", "osu-getseasonal.php", "osu-markasread.php" })
			app.MapGet("/web/" + stub, () => Results.Text(stub == "osu-getseasonal.php" ? "[]" : string.Empty));
	}

	private static IResult Text(string text, int status = 200)
	{
		return Results.Text(text, "text/html; charset=UTF-8", Encoding.UTF8, status);
	}

	private static async Task<IResult> GetScoresAsync(HttpContext context, WebAuthentication auth,
		IBeatmapRepository beatmaps, IScoreRepository scores, IUserRepository users, CancellationToken ct)
	{
		var query = context.Request.Query;
		if (await auth.AuthenticateAsync(context, query["us"], query["ha"], ct) is not { } player) return Results.Unauthorized();
		if (!Md5Text.TryParse(query["c"], out var hash)) return Text("-1|false");
		if (await beatmaps.GetAsync(hash, ct) is not { } map) return Text("-1|false");
		_ = int.TryParse(query["mods"], out var mods);
		var mode = ParseMode(query["m"]);
		var rankedType = int.TryParse(query["v"], out var v) ? v : 1;
		var page = await scores.ListAsync(
			new ScoreQuery(BeatmapHash: hash, Mods: rankedType == 3 ? (GameMods)mods : null, BestPerPlayer: true),
			new PageRequest(0, 50), ct);
		var rows = new List<string>();
		foreach (var (score, index) in page.Items.Where(score => score.Value.Mode == mode).Select((score, i) => (score, i)))
			rows.Add(await ScoreRowAsync(score, index, users, ct));

		var lines = new List<string>
		{
			$"{(int)map.Beatmapset.Status}|false|{map.Id}|{map.Beatmapset.Id}|{rows.Count}|0",
			"0", map.Name, "10.0", string.Empty
		};
		lines.AddRange(rows);
		return Text(string.Join('\n', lines));
	}

	private static GameMode ParseMode(string? text)
	{
		return int.TryParse(text, out var m) && Enum.IsDefined((GameMode)m) ? (GameMode)m : GameMode.Standard;
	}

	private static async Task<string> ScoreRowAsync(ScoreRecord score, int index, IUserRepository users, CancellationToken ct)
	{
		var name = score.Value.UserId is { } id && await users.GetAsync(id, ct) is { } user ? user.Value.Name : "?";
		var hits = score.Value.HitCounts;
		return string.Create(CultureInfo.InvariantCulture,
			$"{score.Id}|{name}|{score.Value.TotalScore}|{score.Value.MaxCombo}|{hits.Num50}|{hits.Num100}|{hits.Num300}|{hits.NumMiss}|{hits.NumKatu}|{hits.NumGeki}|{(score.Value.IsFullCombo ? 1 : 0)}|{(int)score.Value.Mods}|{score.Value.UserId}|{index + 1}|{score.Value.Timestamp.ToUnixTimeSeconds()}|1");
	}

	private static async Task<IResult> GetFriendsAsync(HttpContext context, WebAuthentication auth, CancellationToken ct)
	{
		var query = context.Request.Query;
		if (await auth.AuthenticateAsync(context, query["u"], query["h"], ct) is not { } player) return Results.Unauthorized();
		var (_, friends) = await player.User.ScopeTo(player).Relationships.FriendsAsync(ct);
		return Text(string.Join('\n', friends.Select(friend => friend.Id)));
	}

	private static async Task<IResult> GetBeatmapInfoAsync(HttpContext context, WebAuthentication auth,
		IBeatmapRepository beatmaps, CancellationToken ct)
	{
		var query = context.Request.Query;
		if (await auth.AuthenticateAsync(context, query["u"], query["h"], ct) is null) return Results.Unauthorized();
		var request = await JsonSerializer.DeserializeAsync<BeatmapInfoBody>(context.Request.Body, cancellationToken: ct)
		              ?? new BeatmapInfoBody();
		var lines = new List<string>();
		for (var index = 0; index < request.Filenames.Length; index++)
		{
			var page = await beatmaps.ListAsync(new BeatmapQuery(FileName: request.Filenames[index]), new PageRequest(0, 1), ct);
			if (page.Items.FirstOrDefault() is { } map)
				lines.Add($"{index}|{map.Id}|{map.Beatmapset.Id}|0|{(int)map.Beatmapset.Status}|N|N|N|N|{map.Hash}");
		}

		return Text(string.Join('\n', lines));
	}

	private sealed record BeatmapInfoBody
	{
		public string[] Filenames { get; init; } = [];
	}

	private static async Task<IResult> SubmitAsync(HttpContext context, UserRegistry registry, ScoreSubmitter submitter,
		AnticheatReporter anticheat, IBeatmapRepository beatmaps, WebAuthentication auth, CancellationToken ct)
	{
		if (!context.Request.HasFormContentType) return Text("error: no");
		var form = await context.Request.ReadFormAsync(ct);
		try
		{
			var version = form["osuver"].ToString();
			var iv = form["iv"].ToString();
			var data = ScoreSubmission.Parse(ScoreSubmissionCipher.Decrypt(form["score"].First()!, iv, version));
			if (await auth.AuthenticateAsync(context, data.Username.TrimEnd(), form["pass"], ct) is not { } client)
				return Text("error: no");
			var clientHashText = ScoreSubmissionCipher.Decrypt(form["s"].ToString(), iv, version);
			var hash = ClientHash.Parse(clientHashText);
			var ids = form["c1"].ToString().Split('|');
			var fingerprint = Fingerprint(hash);
			var beatmapHash = new Md5(data.BeatmapHash);
			var submittedClient = new SubmittedClient(clientHashText, fingerprint, ids[0], ids.ElementAtOrDefault(1) ?? string.Empty,
				version[..8], beatmapHash);
			var known = await beatmaps.GetAsync(beatmapHash, ct);
			Md5? storyboard = Md5Text.TryParse(form["sbk"], out var sb) ? sb : (Md5?)null;
			var submission = ToSubmission(data, beatmapHash);
			anticheat.Report(client, submission.Flags);
			await using var replay = OpenReplay(context);
			var (result, _) = await submitter.ScopeTo(client).SubmitAsync(submission,
				known is null ? null : new BeatmapChecksums(known.Hash, storyboard), submittedClient, replay, ct);
			return Text(result.IsOk ? "beatmapId:0|beatmapSetId:0|beatmapPlaycount:0|beatmapPasscount:0|approvedDate:\n" : "error: no");
		}
		catch (Exception ex) when (ex is FormatException or ArgumentException or InvalidDataException or OverflowException)
		{
			return Text("error: no");
		}
	}

	private static OsuFingerprint Fingerprint(ClientHash hash)
	{
		return new OsuFingerprint(new Md5(hash.OsuPathHash),
			new Md5(hash.UninstallHash), new Md5(hash.DiskSignatureHash), new NetworkAdapters { Adapters = hash.Adapters, Hash = new Md5(hash.AdaptersHash) });
	}

	private static Submission ToSubmission(ScoreSubmission data, Md5 beatmapHash)
	{
		var score = new ScoreData(null, beatmapHash, (GameMode)data.Mode, (GameMods)data.Mods,
			new HitCounts(data.Num300, data.Num100, data.Num50, data.NumGeki, data.NumKatu, data.NumMiss),
			data.TotalScore, (short)data.MaxCombo, (short)data.MaxCombo, ParseGrade(data.Grade), data.IsPassed,
			data.IsFullCombo, data.Timestamp);
		return new Submission { Score = score, Flags = (OsuFlags)data.ClientFlags, HashByClient = new Md5(data.Checksum) };
	}

	private static Stream? OpenReplay(HttpContext context)
	{
		return context.Request.Form.Files.GetFile("score") is { } file ? file.OpenReadStream() : null;
	}

	private static Grade ParseGrade(string grade)
	{
		return Enum.TryParse<Grade>(grade, true, out var parsed) && Enum.IsDefined(parsed) ? parsed : Grade.N;
	}

	private static async Task<IResult> GetReplayAsync(HttpContext context, WebAuthentication auth, IScoreRepository scores,
		IReplayStorage replays, CancellationToken ct)
	{
		var query = context.Request.Query;
		if (await auth.AuthenticateAsync(context, query["u"], query["h"], ct) is null) return Results.Unauthorized();
		if (!int.TryParse(query["c"], out var id) || await scores.GetAsync(id, ct) is not { } score ||
		    await replays.OpenAsync(score, ct) is not { } stream)
			return Results.NotFound();

		return Results.File(stream, "application/x-osu-replay");
	}

	private static async Task<IResult> ScreenshotAsync(HttpContext context, WebAuthentication auth, Server server,
		CancellationToken ct)
	{
		var form = await context.Request.ReadFormAsync(ct);
		if (await auth.AuthenticateAsync(context, form["u"], form["p"], ct) is not { } player) return Results.Unauthorized();
		if (form.Files.GetFile("ss") is not { } file) return Results.BadRequest();
		await using var stream = file.OpenReadStream();
		var (reference, result) = await server.ScopeTo(player).Screenshots.UploadAsync(stream, ct);
		return result.IsOk ? Text(reference?.ToString() ?? string.Empty) : Text(string.Empty, result.Outcome == ServerOutcome.DimensionsTooLarge ? 413 : 400);
	}

	private static async Task<IResult> LastFmAsync(HttpContext context, WebAuthentication auth, AnticheatReporter anticheat,
		CancellationToken ct)
	{
		var query = context.Request.Query;
		if (await auth.AuthenticateAsync(context, query["us"], query["ha"], ct) is not { } player) return Results.Unauthorized();
		var b = query["b"].ToString();
		// bancho.py: a leading "a" marks the flags the client detected; the rest is the flag bitmask.
		if (!b.StartsWith('a') || !int.TryParse(b.AsSpan(1), out var flags)) return Text(string.Empty);
		var signs = anticheat.Report(player, (OsuFlags)flags);
		return Text(signs == OsuFlags.Clean ? string.Empty : "-3");
	}

	private static async Task<IResult> SearchAsync(HttpContext context, WebAuthentication auth,
		IBeatmapsetRepository sets, CancellationToken ct)
	{
		var query = context.Request.Query;
		if (await auth.AuthenticateAsync(context, query["u"], query["h"], ct) is not { } player) return Results.Unauthorized();
		if (!auth.MayUseDirect(player)) return Results.StatusCode(StatusCodes.Status403Forbidden);
		var page = await sets.ListAsync(new BeatmapQuery(Text: query["q"]), new PageRequest(0, 100), ct);
		return Text(string.Join('\n', new[] { page.Items.Count.ToString(CultureInfo.InvariantCulture) }
			.Concat(page.Items.Select(DirectLine))));
	}

	private static async Task<IResult> SearchSetAsync(HttpContext context, WebAuthentication auth,
		IBeatmapsetRepository sets, CancellationToken ct)
	{
		var query = context.Request.Query;
		if (await auth.AuthenticateAsync(context, query["u"], query["h"], ct) is not { } player) return Results.Unauthorized();
		if (!auth.MayUseDirect(player)) return Results.StatusCode(StatusCodes.Status403Forbidden);
		if (!int.TryParse(query["s"], out var id) || await sets.GetAsync(id, ct) is not { } set) return Text(string.Empty);
		return Text(DirectLine(set));
	}

	/// <summary>Serves a beatmapset from local storage, else redirects to the configured mirror (osu!direct).</summary>
	private static async Task<IResult> DownloadAsync(HttpContext context, string setId, WebAuthentication auth,
		IBeatmapsetRepository sets, IBeatmapsetStorage storage, IBeatmapsetMirror mirror, CancellationToken ct)
	{
		var query = context.Request.Query;
		if (await auth.AuthenticateAsync(context, query["u"], query["h"], ct) is not { } player) return Results.Unauthorized();
		if (!auth.MayUseDirect(player)) return Results.StatusCode(StatusCodes.Status403Forbidden);
		var withVideo = !setId.EndsWith('n');
		if (!int.TryParse(setId.TrimEnd('n'), out var id)) return Results.NotFound();
		if (await sets.GetAsync(id, ct) is { } set && await storage.OpenArchiveAsync(set, withVideo, ct) is { } archive)
			return Results.File(archive, "application/x-osu-beatmap-archive", $"{id}.osz");
		return await mirror.GetDownloadAddressAsync(id, withVideo, ct) is { } address
			? Results.Redirect(address.ToString())
			: Results.NotFound();
	}

	private static string DirectLine(BeatmapsetRecord set)
	{
		var meta = set.Metadata;
		return $"{set.Id}.osz|{meta.Artist}|{meta.Title}|{meta.Creator}|{(int)set.Status}|10.0|{set.UpdatedAt:yyyy-MM-dd HH:mm:ss}|{set.Id}|0|0|0|0|0";
	}

	private static async Task<IResult> RegisterAsync(HttpContext context, UserRegistry registry, ISettingsRepository settings,
		RateLimiter limiter, CancellationToken ct)
	{
		var form = await context.Request.ReadFormAsync(ct);
		var name = form["user[username]"].ToString();
		var password = form["user[password]"].ToString();
		var checkOnly = form["check"] != "0";
		var failure = await registry.CheckRegistrationAsync(
			new RegisterAttempt(name, new Md5(Encoding.UTF8.GetBytes(password))), ct);
		if (failure is not null)
			return Results.Json(new { form_error = new { user = new { username = new[] { failure.ToString() } } } },
				statusCode: 400);
		if (checkOnly) return Text("ok");
		var rules = (await settings.GetAsync(ct)).RateLimits.Registration;
		var ip = ClientAddress.Resolve(context);
		if (!limiter.TryAcquire($"register:{ip}", rules.PerIp.Count, rules.PerIp.Window) ||
		    !limiter.TryAcquire("register:server", rules.PerServer.Count, rules.PerServer.Window))
			return Results.Json(new { form_error = new { user = new { username = new[] { "Too many registrations." } } } },
				statusCode: 429);
		var (_, error) = await registry.RegisterAsync(
			new RegisterAttempt(name, new Md5(Encoding.UTF8.GetBytes(password))), ct);
		return error is null ? Text("ok") : Results.Json(new { form_error = new { user = new { username = new[] { error.ToString() } } } },
			statusCode: 400);
	}
}

internal static class Md5Text
{
	public static bool TryParse(string? text, out Md5 hash)
	{
		hash = default;
		if (text is not { Length: 32 }) return false;
		try
		{
			hash = new Md5(text);
			return true;
		}
		catch (ArgumentException)
		{
			return false;
		}
	}
}
