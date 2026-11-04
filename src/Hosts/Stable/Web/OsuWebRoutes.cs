using Basil.Application.Servers.Messages;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Basil.Application.Anticheat.Services;
using Basil.Application.Beatmaps.Persistence;
using Basil.Application.Beatmaps.Ports;
using Basil.Application.Beatmaps.Storage;
using Basil.Application.Common;
using Basil.Application.Content.Persistence;
using Basil.Application.Scores.Messages;
using Basil.Application.Scores.Persistence;
using Basil.Application.Scores.Storage;
using Basil.Application.Servers.Objects;
using Basil.Application.Users.Messages;
using Basil.Application.Users.Objects.Connections;
using Basil.Application.Users.Persistence;
using Basil.Application.Users.Registry;
using Basil.Domain.Beatmaps;
using Basil.Domain.Client;
using Basil.Domain.Common;
using Basil.Domain.Mechanics;
using Basil.Domain.Scores;
using Basil.Domain.Users;
using Basil.Gateway;
using Mediator;
using Basil.Host.Stable.Packets;
using Basil.Host.Stable.Support;
using Basil.Protocol.Stable.Models.Client;
using Basil.Protocol.Stable.Models.Scores;
using Basil.Protocol.Stable.Wire.Security;

namespace Basil.Host.Stable.Web;

/// <summary>The web endpoints of the osu! client (<c>osu.</c>), kept as the client expects them.</summary>
internal static class OsuWebRoutes
{
	/// <summary>How many name checks one address may make in <see cref="RegistrationCheckWindow" />.</summary>
	internal const int RegistrationChecksPerWindow = 20;

	private static readonly TimeSpan RegistrationCheckWindow = TimeSpan.FromMinutes(10);

	/// <summary>Maps the web endpoints; each one declares its admission policy.</summary>
	/// <param name="app">The route builder of the <c>osu.</c> host.</param>
	public static void MapOsuWeb(this IEndpointRouteBuilder app)
	{
		var web = app.MapGroup("").WithGroupName(StableDocumentation.Group).WithTags("Web")
			.WithMetadata(new AdmissionPolicyAttribute());
		web.MapGet("/", () => "osu").WithSummary("Say that the web host of the osu! client is there.");
		web.MapGet("/web/osu-osz2-getscores.php", GetScoresAsync)
			.WithSummary("List the best scores of a beatmap.")
			.WithDescription("""
			                 Query: `us` (username), `ha` (password hash), `c` (beatmap hash), `m` (mode), `v` (leaderboard type), `mods`.
			                 The answer is a text: a header line with the ranked status and the beatmap, then one line per score.
			                 An unknown beatmap is answered `-1|false`; wrong credentials are answered 401.
			                 """);
		web.MapGet("/web/osu-getfriends.php", GetFriendsAsync)
			.WithSummary("List the ids of the player's friends.")
			.WithDescription("Query: `u` (username), `h` (password hash). The answer is one id per line.");
		web.MapPost("/web/osu-getbeatmapinfo.php", GetBeatmapInfoAsync)
			.WithSummary("Say which of the listed beatmap files the server knows.")
			.WithDescription("""
			                 Query: `u`, `h`. The JSON body lists the file names; the answer has one line per known file.
			                 The number of file names answered is capped.
			                 """);
		web.MapPost("/web/osu-submit-modular-selector.php", SubmitAsync)
			.WithSummary("Submit a score.")
			.WithDescription("""
			                 A multipart form: the encrypted score (`score`, with `iv` and `osuver`), the encrypted client hash (`s`), `pass`,
			                 `c1`, `sbk` and the replay. A score that is accepted is answered with a line of beatmap statistics; anything
			                 else (wrong credentials, a score that cannot be read, a refused score) is answered `error: no`.
			                 """);
		web.MapGet("/web/osu-getreplay.php", GetReplayAsync)
			.WithSummary("Download the replay of a score.")
			.WithDescription("Query: `u`, `h`, `c` (score id). The answer is the replay file, or 404.");
		web.MapPost("/web/osu-screenshot.php", ScreenshotAsync)
			.WithSummary("Upload a screenshot.")
			.WithDescription("""
			                 A multipart form with `u`, `p` and the picture `ss`. The answer is the reference of the stored screenshot;
			                 400 is a refused picture and 413 one that is too large.
			                 """);
		web.MapGet("/web/lastfm.php", LastFmAsync)
			.WithSummary("Receive the client's cheat-detection flags.")
			.WithDescription("Query: `us`, `ha`, `b` (the flags, written `a` followed by a number).");
		web.MapGet("/web/osu-search.php", SearchAsync)
			.WithSummary("Search beatmapsets for osu!direct.")
			.WithDescription("Query: `u`, `h`, `q` (the search). Needs the right to use osu!direct, otherwise 403.");
		web.MapGet("/web/osu-search-set.php", SearchSetAsync)
			.WithSummary("Look up one beatmapset for osu!direct.")
			.WithDescription("Query: `u`, `h`, `s` (set id). Needs the right to use osu!direct, otherwise 403.");
		web.MapGet("/d/{setId}", DownloadAsync)
			.WithSummary("Download a beatmapset archive.")
			.WithDescription("""
			                 Query: `u`, `h`. The id may end with `n` (without video). Answers the archive, or redirects to where it can
			                 be downloaded; needs the right to use osu!direct, otherwise 403.
			                 """);
		web.MapPost("/users", RegisterAsync)
			.WithSummary("Register an account, or only check that the details are acceptable.")
			.WithDescription("""
			                 A form with `user[username]`, `user[user_email]`, `user[password]` and `check` (anything but `0` only checks
			                 and is answered `ok`). Refusals are answered with a JSON `form_error`. Checks from one address are limited,
			                 then answered 429.
			                 """);
		foreach (var stub in new[]
			         { "bancho_connect.php", "check-updates.php", "osu-getseasonal.php", "osu-markasread.php" })
			web.MapGet("/web/" + stub, () => Results.Text(stub == "osu-getseasonal.php" ? "[]" : string.Empty))
				.WithSummary($"Answer the client's call to {stub} with nothing to say.")
				.WithDescription(
					"Basil has nothing to offer here; the answer is empty (`[]` for the seasonal backgrounds).");
	}

	private static IResult Text(string text, int status = 200)
	{
		return Results.Text(text, "text/html; charset=UTF-8", Encoding.UTF8, status);
	}

	private static async Task<IResult> GetScoresAsync(HttpContext context, WebAuthentication auth,
		IBeatmapRepository beatmaps, IScoreRepository scores, IUserRepository users, CancellationToken ct)
	{
		var query = context.Request.Query;
		if (await auth.AuthenticateAsync(context, query["us"], query["ha"], ct) is null)
			return Results.Unauthorized();
		if (!Md5Text.TryParse(query["c"], out var hash)) return Text("-1|false");
		if (await beatmaps.GetAsync(hash, ct) is not { } map) return Text("-1|false");
		_ = int.TryParse(query["mods"], out var mods);
		var mode = ParseMode(query["m"]);
		var rankedType = int.TryParse(query["v"], out var v) ? v : 1;
		var page = await scores.ListAsync(
			new ScoreQuery(BeatmapHash: hash, Mods: rankedType == 3 ? WireMap.Mods(mods) : null,
				BestPerPlayer: true),
			new PageRequest(0, 50), ct);
		var rows = new List<string>();
		foreach (var (score, index) in page.Items.Where(score => score.Value.Mode == mode)
			         .Select((score, i) => (score, i)))
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
		return int.TryParse(text, out var m) && WireMap.Known<GameMode>(m) is { } mode
			? mode
			: GameMode.Standard;
	}

	private static async Task<string> ScoreRowAsync(ScoreRecord score, int index, IUserRepository users,
		CancellationToken ct)
	{
		var name = score.Value.UserId is { } id && await users.GetAsync(id, ct) is { } user ? user.Value.Name : "?";
		var hits = score.Value.LastState.HitCounts;
		return string.Create(CultureInfo.InvariantCulture,
			$"{score.Id}|{name}|{score.Value.LastState.TotalScore}|{score.Value.LastState.MaxCombo}|{hits.Num50}|{hits.Num100}|{hits.Num300}|{hits.NumMiss}|{hits.NumKatu}|{hits.NumGeki}|{(score.Value.LastState.IsFullCombo ? 1 : 0)}|{(int)score.Value.Mods}|{score.Value.UserId}|{index + 1}|{score.Value.Timestamp.ToUnixTimeSeconds()}|1");
	}

	private static async Task<IResult> GetFriendsAsync(HttpContext context, WebAuthentication auth, ISender sender,
		CancellationToken ct)
	{
		var query = context.Request.Query;
		if (await auth.AuthenticateAsync(context, query["u"], query["h"], ct) is not { } player)
			return Results.Unauthorized();
		var (_, friends) = await sender.Send(new GetFriends(player), ct);
		return Text(string.Join('\n', friends.Select(friend => friend.Id)));
	}

	private static async Task<IResult> GetBeatmapInfoAsync(HttpContext context, WebAuthentication auth,
		IBeatmapRepository beatmaps, CancellationToken ct)
	{
		var query = context.Request.Query;
		if (await auth.AuthenticateAsync(context, query["u"], query["h"], ct) is null) return Results.Unauthorized();
		var request =
			await JsonSerializer.DeserializeAsync<BeatmapInfoBody>(context.Request.Body, cancellationToken: ct)
			?? new BeatmapInfoBody();
		var lines = new List<string>();
		for (var index = 0; index < Math.Min(request.Filenames.Length, PacketHandlers.MaxRequestedFilenames); index++)
		{
			var page = await beatmaps.ListAsync(new BeatmapQuery(FileName: request.Filenames[index]),
				new PageRequest(0, 1), ct);
			if (page.Items.FirstOrDefault() is { } map)
				lines.Add(
					$"{index}|{map.Id}|{map.Beatmapset.Id}|0|{(int)map.Beatmapset.Status}|N|N|N|N|{map.Hash}");
		}

		return Text(string.Join('\n', lines));
	}

	private static async Task<IResult> SubmitAsync(HttpContext context, UserRegistry registry, ISender sender,
		IAnticheatReporter anticheat, IBeatmapRepository beatmaps, WebAuthentication auth, ISettingsRepository settings,
		CancellationToken ct)
	{
		// A score submission carries a replay, which is far below the screenshot limit.
		await UploadLimit.AllowAsync(context, settings, uploads => uploads.ScreenshotMaxSize);
		try
		{
			var form = await context.Request.ReadFormAsync(ct);
			var version = form["osuver"].ToString();
			var iv = form["iv"].ToString();
			var data = ScoreSubmission.Parse(ScoreSubmissionCipher.Decrypt(form["score"].First()!, iv, version));
			if (await auth.AuthenticateAsync(context, data.Username.TrimEnd(), form["pass"], ct) is not { } client)
				return Text("error: no");
			var clientHashText = ScoreSubmissionCipher.Decrypt(form["s"].ToString(), iv, version);
			var fingerprint = OsuFingerprintExtensions.Parse(clientHashText);
			var ids = form["c1"].ToString().Split('|');
			var beatmapHash = new Md5(data.BeatmapHash);
			var submittedClient = new SubmittedClient(clientHashText, fingerprint, ids[0],
				ids.ElementAtOrDefault(1) ?? string.Empty,
				version[..8], beatmapHash);
			var known = await beatmaps.GetAsync(beatmapHash, ct);
			var storyboard = Md5Text.TryParse(form["sbk"], out var sb) ? sb : (Md5?)null;
			var submission = ToSubmission(data, beatmapHash);
			anticheat.Report(client, submission.Flags);
			await using var replay = OpenReplay(context);
			var (result, _) = await sender.Send(new SubmitScore(client, submission,
				known is null ? null : new BeatmapChecksums(known.Hash, storyboard), submittedClient, replay), ct);
			return Text(result.IsOk
				? "beatmapId:0|beatmapSetId:0|beatmapPlaycount:0|beatmapPasscount:0|approvedDate:\n"
				: "error: no");
		}
		catch (Exception ex) when (ex is FormatException or ArgumentException or InvalidDataException
			                           or OverflowException or InvalidOperationException)
		{
			return Text("error: no");
		}
	}

	private static Submission ToSubmission(ScoreSubmission data, Md5 beatmapHash)
	{
		var score = new ScoreData(null, beatmapHash, WireMap.Known<GameMode>(data.Mode) ?? GameMode.Standard,
			WireMap.Mods(data.Mods),
			new ScoreState(new HitCounts(data.Num300, data.Num100, data.Num50, data.NumGeki, data.NumKatu, data.NumMiss),
				data.TotalScore, checked((ushort)data.MaxCombo), checked((ushort)data.MaxCombo), data.Perfect),
			ParseGrade(data.Grade), data.IsPassed, data.Timestamp);
		return new Submission
			{ Score = score, Flags = data.ClientFlags, HashByClient = new Md5(data.Checksum) };
	}

	private static Stream? OpenReplay(HttpContext context)
	{
		return context.Request.Form.Files.GetFile("score") is { } file ? file.OpenReadStream() : null;
	}

	private static Grade ParseGrade(string grade)
	{
		return Enum.TryParse<Grade>(grade, true, out var parsed) && Enum.IsDefined(parsed) ? parsed : Grade.N;
	}

	private static async Task<IResult> GetReplayAsync(HttpContext context, WebAuthentication auth,
		IScoreRepository scores,
		IReplayStorage replays, CancellationToken ct)
	{
		var query = context.Request.Query;
		if (await auth.AuthenticateAsync(context, query["u"], query["h"], ct) is null) return Results.Unauthorized();
		if (!int.TryParse(query["c"], out var id) || await scores.GetAsync(id, ct) is not { } score ||
		    await replays.OpenFramesAsync(score, ct) is not { } stream)
			return Results.NotFound();

		return Results.File(stream, "application/x-osu-replay");
	}

	private static async Task<IResult> ScreenshotAsync(HttpContext context, WebAuthentication auth, ISender sender,
		ISettingsRepository settings, CancellationToken ct)
	{
		await UploadLimit.AllowAsync(context, settings, uploads => uploads.ScreenshotMaxSize);
		IFormCollection form;
		try
		{
			form = await context.Request.ReadFormAsync(ct);
		}
		catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException)
		{
			return Results.BadRequest();
		}

		if (await auth.AuthenticateAsync(context, form["u"], form["p"], ct) is not { } player)
			return Results.Unauthorized();
		if (form.Files.GetFile("ss") is not { } file) return Results.BadRequest();
		await using var stream = file.OpenReadStream();
		var (reference, result) = await sender.Send(new UploadScreenshot(player, stream), ct);
		return result.IsOk
			? Text(reference?.ToString() ?? string.Empty)
			: Text(string.Empty, result.Outcome == ServerOutcome.DimensionsTooLarge ? 413 : 400);
	}

	private static async Task<IResult> LastFmAsync(HttpContext context, WebAuthentication auth,
		IAnticheatReporter anticheat,
		CancellationToken ct)
	{
		var query = context.Request.Query;
		if (await auth.AuthenticateAsync(context, query["us"], query["ha"], ct) is not { } player)
			return Results.Unauthorized();
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
		if (await auth.AuthenticateAsync(context, query["u"], query["h"], ct) is not { } player)
			return Results.Unauthorized();
		if (!auth.MayUseDirect(player)) return Results.StatusCode(StatusCodes.Status403Forbidden);
		var page = await sets.ListAsync(new BeatmapQuery(query["q"]).LimitTo(auth.Effective(player)),
			new PageRequest(0, 100), ct);
		return Text(string.Join('\n', new[] { page.Items.Count.ToString(CultureInfo.InvariantCulture) }
			.Concat(page.Items.Select(DirectLine))));
	}

	private static async Task<IResult> SearchSetAsync(HttpContext context, WebAuthentication auth,
		IBeatmapsetRepository sets, CancellationToken ct)
	{
		var query = context.Request.Query;
		if (await auth.AuthenticateAsync(context, query["u"], query["h"], ct) is not { } player)
			return Results.Unauthorized();
		if (!auth.MayUseDirect(player)) return Results.StatusCode(StatusCodes.Status403Forbidden);
		if (!int.TryParse(query["s"], out var id) || await sets.GetAsync(id, ct) is not { } set ||
		    !MayView(auth, player, set))
			return Text(string.Empty);
		return Text(DirectLine(set));
	}

	/// <summary>Serves a beatmapset from local storage, else redirects to the configured mirror (osu!direct).</summary>
	private static async Task<IResult> DownloadAsync(HttpContext context, string setId, WebAuthentication auth,
		IBeatmapsetRepository sets, IBeatmapsetStorage storage, IBeatmapsetMirror mirror, CancellationToken ct)
	{
		var query = context.Request.Query;
		if (await auth.AuthenticateAsync(context, query["u"], query["h"], ct) is not { } player)
			return Results.Unauthorized();
		if (!auth.MayUseDirect(player)) return Results.StatusCode(StatusCodes.Status403Forbidden);
		var withVideo = !setId.EndsWith('n');
		if (!int.TryParse(setId.TrimEnd('n'), out var id)) return Results.NotFound();
		var set = await sets.GetAsync(id, ct);
		if (set is not null && !MayView(auth, player, set)) return Results.NotFound();
		if (set is not null && await storage.OpenArchiveAsync(set, withVideo, ct) is { } archive)
			return Results.File(archive, "application/x-osu-beatmap-archive", $"{id}.osz");
		return await mirror.GetDownloadAddressAsync(id, withVideo, ct) is { } address
			? Results.Redirect(address.ToString())
			: Results.NotFound();
	}

	private static bool MayView(WebAuthentication auth, StableConnection player, BeatmapsetRecord set)
	{
		return set.Visible || auth.Effective(player).Allows(UserPermissions.BeatmapsetManageViewHidden);
	}

	private static string DirectLine(BeatmapsetRecord set)
	{
		var meta = set.Metadata;
		return
			$"{set.Id}.osz|{meta.Artist}|{meta.Title}|{meta.Creator}|{(int)set.Status}|10.0|{set.UpdatedAt:yyyy-MM-dd HH:mm:ss}|{set.Id}|0|0|0|0|0";
	}

	private static async Task<IResult> RegisterAsync(HttpContext context, UserRegistry registry,
		ISettingsRepository settings,
		RateLimiter limiter, CancellationToken ct)
	{
		IFormCollection form;
		try
		{
			form = await context.Request.ReadFormAsync(ct);
		}
		catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException)
		{
			return Results.BadRequest();
		}

		var name = form["user[username]"].ToString();
		var password = form["user[password]"].ToString();
		var checkOnly = form["check"] != "0";
		var rules = (await settings.GetAsync(ct)).RateLimits.Registration;
		var ip = ClientAddress.Resolve(context);
		if (checkOnly &&
		    !limiter.TryAcquire($"register-check:{ip}", RegistrationChecksPerWindow, RegistrationCheckWindow))
			return Results.Json(
				new { form_error = new { user = new { username = new[] { "Too many registrations." } } } },
				statusCode: 429);
		var failure = await registry.CheckRegistrationAsync(
			new RegisterAttempt(name, new Md5(Encoding.UTF8.GetBytes(password))), ct);
		if (failure is not null)
			return Results.Json(new { form_error = new { user = new { username = new[] { failure.ToString() } } } },
				statusCode: 400);
		if (checkOnly) return Text("ok");
		if (!limiter.TryAcquire($"register:{ip}", rules.PerIp.Count, rules.PerIp.Window) ||
		    !limiter.TryAcquire("register:server", rules.PerServer.Count, rules.PerServer.Window))
			return Results.Json(
				new { form_error = new { user = new { username = new[] { "Too many registrations." } } } },
				statusCode: 429);
		var (_, error) = await registry.RegisterAsync(
			new RegisterAttempt(name, new Md5(Encoding.UTF8.GetBytes(password))), ct);
		return error is null
			? Text("ok")
			: Results.Json(new { form_error = new { user = new { username = new[] { error.ToString() } } } },
				statusCode: 400);
	}

	private sealed record BeatmapInfoBody
	{
		public string[] Filenames { get; init; } = [];
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