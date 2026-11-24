using Basil.Application.Sessions.Persistence;
using Basil.Application.Sessions.Registry;
using Basil.Domain.Users;
using Basil.Domain.Content;
using Basil.Domain;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Basil.Application.Anticheat.Services;
using Basil.Application.Beatmaps.Messages;
using Basil.Application.Beatmaps.Persistence;
using Basil.Application.Beatmaps.Storage;
using Basil.Application.Content.Messages;
using Basil.Application.Rulesets.Messages;
using Basil.Application.Scores.Messages;
using Basil.Application.Scores.Objects;
using Basil.Application.Scores.Persistence;
using Basil.Application.Servers.Messages.Content;
using Basil.Application.Servers.Objects;
using Basil.Application.Shared.Paging;
using Basil.Application.Shared.Settings;
using Basil.Application.Users.Messages;
using Basil.Domain.Beatmaps;
using Basil.Domain.Client;
using Basil.Domain.Game;
using Basil.Domain.Scores;
using Basil.Gateway.Admission;
using Basil.Gateway.Limits;
using Basil.Gateway.RateLimiting;
using Basil.Host.Stable.Packets;
using Basil.Host.Stable.Support;
using Basil.Protocol.Game.Stable.Models.Client;
using Basil.Protocol.Game.Stable.Models.Scores;
using Basil.Protocol.Game.Stable.Wire.Security;
using Basil.Shared.Structures.Cryptography;
using Mediator;

namespace Basil.Host.Stable.Web;

internal static class OsuWebRoutes
{
	internal const int RegistrationChecksPerWindow = 20;

	private static readonly TimeSpan RegistrationCheckWindow = TimeSpan.FromMinutes(10);

	public static void MapOsuWeb(this IEndpointRouteBuilder app)
	{
		var web = app.MapGroup("")
			.WithMetadata(new AdmissionPolicyAttribute()).AddEndpointFilter<StableAdmission>()
			.AddEndpointFilter(async (invocation, next) =>
			{
				try
				{
					return await next(invocation);
				}
				catch (BasilException)
				{
					return Results.BadRequest();
				}
			});
		web.MapGet("/", () => Results.Redirect("https://github.com/thnhmai06/osuBasil"));
		web.MapGet("/web/osu-osz2-getscores.php", GetScoresAsync)
			.WithMetadata(new AdmissionPolicyAttribute { Class = WorkClass.Listing });
		web.MapGet("/web/osu-getfriends.php", GetFriendsAsync);
		web.MapPost("/web/osu-getbeatmapinfo.php", GetBeatmapInfoAsync);
		web.MapPost("/web/osu-submit-modular-selector.php", SubmitAsync)
			.WithMetadata(new AdmissionPolicyAttribute { Class = WorkClass.Upload });
		web.MapGet("/web/osu-getreplay.php", GetReplayAsync);
		web.MapPost("/web/osu-screenshot.php", ScreenshotAsync)
			.WithMetadata(new AdmissionPolicyAttribute { Class = WorkClass.Upload });
		web.MapGet("/web/lastfm.php", LastFmAsync);
		web.MapGet("/web/osu-search.php", SearchAsync)
			.WithMetadata(new AdmissionPolicyAttribute { Class = WorkClass.Listing });
		web.MapGet("/web/osu-search-set.php", SearchSetAsync)
			.WithMetadata(new AdmissionPolicyAttribute { Class = WorkClass.Listing });
		web.MapGet("/d/{setId}", DownloadAsync);
		web.MapGet("/web/maps/{filename}", GetBeatmapFileAsync)
			.WithMetadata(new AdmissionPolicyAttribute { Class = WorkClass.Listing });
		web.MapPost("/difficulty-rating", GetDifficultyRatingAsync);
		web.MapGet("/web/osu-getfavourites.php", GetFavouritesAsync);
		web.MapGet("/web/osu-addfavourite.php", AddFavouriteAsync);
		web.MapGet("/web/osu-rate.php", RateAsync);
		web.MapPost("/web/osu-comment.php", CommentAsync);
		web.MapPost("/users", RegisterAsync);
		web.MapGet("/web/osu-getseasonal.php", GetSeasonalsAsync);
		foreach (var stub in new[] { "bancho_connect.php", "check-updates.php", "osu-markasread.php" })
			web.MapGet("/web/" + stub, () => Results.Text(string.Empty));
	}

	// The client takes absolute URLs; the images are served by the assets host of the domain it called.
	private static async Task<IResult> GetSeasonalsAsync(HttpContext context, ISender sender, CancellationToken ct)
	{
		var domain = context.Request.Host.Host["osu.".Length..];
		var seasonals = await sender.Send(new ListSeasonals(), ct);
		return Results.Json(seasonals.Select(image =>
			$"https://assets.{domain}/menu/seasonals/{Path.GetFileName(image.Value)}"));
	}

	private static IResult Text(string text, int status = 200)
	{
		return Results.Text(text, "text/html; charset=UTF-8", Encoding.UTF8, status);
	}

	private static async Task<IResult> GetScoresAsync(HttpContext context, WebAuthentication auth, ISender sender,
		CancellationToken ct)
	{
		var query = context.Request.Query;
		if (await auth.AuthenticateAsync(context, query["us"], query["ha"], ct) is null)
			return Results.Unauthorized();
		if (!Md5Text.TryParse(query["c"], out var hash)) return Text("-1|false");
		_ = int.TryParse(query["mods"], out var mods);
		var mode = ParseMode(query["m"]);
		var rankedType = int.TryParse(query["v"], out var v) ? v : 1;
		if (await sender.Send(new GetLeaderboard(hash, mode, rankedType == 3 ? WireMap.Mods(mods) : null), ct) is not
		    { } board)
			return Text("-1|false");
		var map = board.Beatmap;
		var rows = board.Entries.Select((entry, index) => ScoreRow(entry.Score, entry.PlayerName, index)).ToList();

		var lines = new List<string>
		{
			$"{(int)map.Beatmapset.Status}|false|{map.Id}|{map.Beatmapset.Id}|{rows.Count}|0",
			"0", map.FullName, "10.0", string.Empty
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

	private static string ScoreRow(ScoreRecord score, string name, int index)
	{
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
		ISender sender, CancellationToken ct)
	{
		var query = context.Request.Query;
		if (await auth.AuthenticateAsync(context, query["u"], query["h"], ct) is null) return Results.Unauthorized();
		var request =
			await JsonSerializer.DeserializeAsync<BeatmapInfoBody>(context.Request.Body, cancellationToken: ct)
			?? new BeatmapInfoBody();
		var lines = new List<string>();
		var maps = await sender.Send(
			new FindBeatmapsByFileNames([.. request.Filenames.Take(PacketHandlers.MaxRequestedFilenames)]), ct);
		for (var index = 0; index < maps.Count; index++)
			if (maps[index] is { } map)
				lines.Add(
					$"{index}|{map.Id}|{map.Beatmapset.Id}|0|{(int)map.Beatmapset.Status}|N|N|N|N|{map.Hash}");

		return Text(string.Join('\n', lines));
	}

	private static async Task<IResult> SubmitAsync(HttpContext context, SessionRegistry registry, ISender sender,
		IAnticheatReporter anticheat, WebAuthentication auth,
		CancellationToken ct)
	{
		// A score submission carries a replay, which is far below the screenshot limit.
		await UploadLimit.AllowAsync(context, sender, uploads => uploads.ScreenshotMaxSize);
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
			var beatmapHash = new Md5Hash(data.BeatmapHash);
			var submittedClient = new SubmittedClient(clientHashText, fingerprint, ids[0],
				ids.ElementAtOrDefault(1) ?? string.Empty,
				version[..8], beatmapHash);
			var known = await sender.Send(new FindBeatmapByHash(beatmapHash), ct);
			var storyboard = Md5Text.TryParse(form["sbk"], out var sb) ? sb : (Md5Hash?)null;
			var submission = ToSubmission(data, beatmapHash);
			anticheat.Report(client, submission.Flags);
			await using var replay = OpenReplay(context);
			var (result, _, receipt) = await sender.Send(new SubmitScore(client, submission,
				known is null ? null : new BeatmapChecksums(known.Hash, storyboard), submittedClient, replay), ct);
			return Text(result == ScoreSubmissionResult.Ok && submission.Score.IsPassed
				? SubmissionCharts.Build(receipt!, known, $"{context.Request.Scheme}://{context.Request.Host}")
				: OsuWebReplies.SubmissionRefused);
		}
		catch (Exception ex) when (ex is FormatException or ArgumentException or InvalidDataException
			                           or OverflowException or InvalidOperationException or BasilException)
		{
			return Text("error: no");
		}
	}

	private static Submission ToSubmission(ScoreSubmission data, Md5Hash beatmapHash)
	{
		var score = new ScoreData(null, beatmapHash, WireMap.Known<GameMode>(data.Mode) ?? GameMode.Standard,
			WireMap.Mods(data.Mods),
			new ScoreState(
				new HitCounts(data.Num300, data.Num100, data.Num50, data.NumGeki, data.NumKatu, data.NumMiss),
				data.TotalScore, checked((ushort)data.MaxCombo), checked((ushort)data.MaxCombo), data.Perfect),
			ParseGrade(data.Grade), data.IsPassed, data.Timestamp);
		return new Submission
			{ Score = score, Flags = data.ClientFlags, HashByClient = new Md5Hash(data.Checksum) };
	}

	private static Stream? OpenReplay(HttpContext context)
	{
		return context.Request.Form.Files.GetFile("score") is { } file ? file.OpenReadStream() : null;
	}

	private static Grade ParseGrade(string grade)
	{
		return Enum.TryParse<Grade>(grade, true, out var parsed) && Enum.IsDefined(parsed) ? parsed : Grade.N;
	}

	private static async Task<IResult> GetReplayAsync(HttpContext context, WebAuthentication auth, ISender sender,
		CancellationToken ct)
	{
		var query = context.Request.Query;
		if (await auth.AuthenticateAsync(context, query["u"], query["h"], ct) is null) return Results.Unauthorized();
		if (!int.TryParse(query["c"], out var id) ||
		    await sender.Send(new OpenReplayFrames(id), ct) is not { } stream)
			return Results.NotFound();

		return Results.File(stream, "application/x-osu-replay");
	}

	private static async Task<IResult> ScreenshotAsync(HttpContext context, WebAuthentication auth, ISender sender,
		CancellationToken ct)
	{
		await UploadLimit.AllowAsync(context, sender, uploads => uploads.ScreenshotMaxSize);
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
		try
		{
			var (reference, result) = await sender.Send(new UploadScreenshot(player, stream), ct);
			return result == ServerActionResult.Ok
				? Text(reference?.ToString() ?? string.Empty)
				: Text(string.Empty, 400);
		}
		catch (ImageDimensionsTooLargeException)
		{
			return Text(string.Empty, 413);
		}
		catch (BasilException)
		{
			return Text(string.Empty, 400);
		}
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

	private static async Task<IResult> SearchAsync(HttpContext context, WebAuthentication auth, ISender sender,
		CancellationToken ct)
	{
		var query = context.Request.Query;
		if (await auth.AuthenticateAsync(context, query["u"], query["h"], ct) is not { } player)
			return Results.Unauthorized();
		if (!auth.MayUseDirect(player)) return Results.StatusCode(StatusCodes.Status403Forbidden);
		if (!int.TryParse(query["r"], out var status) || status is < 0 or > 8 ||
		    !int.TryParse(query["m"], out var mode) || mode is < OsuWebReplies.DirectAllModes or > 3 ||
		    !int.TryParse(query["p"], out var pageNumber) || pageNumber < 0 ||
		    pageNumber > int.MaxValue / OsuWebReplies.DirectPageSize)
			return Results.BadRequest();

		var text = query["q"].ToString();
		var filter = OsuWebReplies.DirectSortTabs.Contains(text.Replace(' ', '+'))
			? new BeatmapQuery()
			: BeatmapQuery.Parse(text);
		filter = filter with
		{
			Mode = mode == OsuWebReplies.DirectAllModes ? filter.Mode : (GameMode)mode,
			Status = status == OsuWebReplies.DirectAllStatuses
				? filter.Status
				: BeatmapsetStatusExtensions.FromOsuDirect(status)
		};
		var page = await sender.Send(
			new SearchBeatmapsets(player,
				filter,
				new PageRequest(pageNumber * OsuWebReplies.DirectPageSize, OsuWebReplies.DirectPageSize)), ct);
		var lines = new List<string>
		{
			(page.Items.Count == OsuWebReplies.DirectPageSize
				? OsuWebReplies.DirectMorePages
				: page.Items.Count).ToString(CultureInfo.InvariantCulture)
		};
		var details = await sender.Send(new GetBeatmapsets(player, [.. page.Items.Select(set => set.Id)]), ct);
		lines.AddRange(details.Select(SearchLine));
		return Text(string.Join('\n', lines));
	}

	private static string SearchLine(BeatmapsetDetail detail)
	{
		var meta = detail.Set.Metadata;
		var diffs = string.Join(',', detail.Beatmaps.OrderBy(map => map.Metadata.Difficulty?.Star ?? 0).Select(map =>
		{
			// The client has no "not calculated yet": it shows zeros.
			var d = map.Metadata.Difficulty ?? default;
			return string.Create(CultureInfo.InvariantCulture,
				$"[{d.Star:0.00}\u2b50] {DirectText(map.Metadata.VersionName)} {{cs: {Decimal(d.Cs)} / od: {Decimal(d.Od)} / ar: {Decimal(d.Ar)} / hp: {Decimal(d.Hp)}}}@{(int)map.Metadata.Mode}");
		}));
		var hasVideo = detail.Beatmaps.Any(map => map.Assets.VideoFile is not null) ? 1 : 0;
		return
			$"{detail.Set.Id}.osz|{DirectText(meta.Artist)}|{DirectText(meta.Title)}|{meta.Creator}|{(int)detail.Set.Status}|10.0|{detail.Set.UpdatedAt:yyyy-MM-dd HH:mm:ss}|{detail.Set.Id}|0|{hasVideo}|0|0|0|{diffs}";
	}

	// The client splits the line at '|', so a difficulty or title cannot carry one; bancho.py writes 'I' instead.
	private static string DirectText(string value)
	{
		return value.Replace('|', 'I');
	}

	// The client shows a stat with a decimal point even when it is whole: 4.0, not 4.
	private static string Decimal(double value)
	{
		var text = value.ToString("R", CultureInfo.InvariantCulture);
		return text.Contains('.') ? text : text + ".0";
	}

	private static async Task<IResult> SearchSetAsync(HttpContext context, WebAuthentication auth, ISender sender,
		CancellationToken ct)
	{
		var query = context.Request.Query;
		if (await auth.AuthenticateAsync(context, query["u"], query["h"], ct) is not { } player)
			return Results.Unauthorized();
		if (!auth.MayUseDirect(player)) return Results.StatusCode(StatusCodes.Status403Forbidden);
		if (!int.TryParse(query["s"], out var id) ||
		    await sender.Send(new GetBeatmapset(player, id), ct) is not { } detail)
			return Text(string.Empty);
		return Text(DirectLine(detail.Set));
	}

	private static async Task<IResult> DownloadAsync(HttpContext context, string setId, WebAuthentication auth,
		ISender sender, CancellationToken ct)
	{
		var query = context.Request.Query;
		if (await auth.AuthenticateAsync(context, query["u"], query["h"], ct) is not { } player)
			return Results.Unauthorized();
		if (!auth.MayUseDirect(player)) return Results.StatusCode(StatusCodes.Status403Forbidden);
		var withVideo = !setId.EndsWith('n');
		if (!int.TryParse(setId.TrimEnd('n'), out var id)) return Results.NotFound();
		if (await sender.Send(new OpenBeatmapsetArchive(player, id, withVideo), ct) is { } archive)
			return Results.File(archive, "application/x-osu-beatmap-archive", $"{id}.osz",
				enableRangeProcessing: true);
		return Results.NotFound();
	}

	private static async Task<IResult> GetBeatmapFileAsync(string filename, ISender sender, CancellationToken ct)
	{
		if (await sender.Send(new FindBeatmapByFileName(filename), ct) is not { } map ||
		    await sender.Send(new OpenBeatmapFile(null, map.Id, BeatmapsetFileType.Version), ct) is not { } file)
			return Results.NotFound();
		return Results.Stream(file.Content, "application/x-osu-beatmap");
	}

	private static async Task<IResult> GetDifficultyRatingAsync(HttpContext context, ISender sender,
		CancellationToken ct)
	{
		var query = context.Request.Query;
		if (!int.TryParse(query["b"], out var id) || !int.TryParse(query["mods"], out var mods))
			return Results.BadRequest();
		if (await sender.Send(new GetBeatmap(null, id), ct) is not { } map ||
		    await sender.Send(new GetBeatmapStars(map.Id, map.Hash, map.Metadata.Mode, (GameMods)mods), ct) is not { } stars)
			return Results.NotFound();
		return Results.Text(
			JsonSerializer.Serialize(new DifficultyRatingBody(map.Id, mods, Math.Round(stars, 2))),
			"application/json");
	}

	private static async Task<IResult> GetFavouritesAsync(HttpContext context, WebAuthentication auth,
		CancellationToken ct)
	{
		var query = context.Request.Query;
		return await auth.AuthenticateAsync(context, query["u"], query["h"], ct) is null
			? Results.Unauthorized()
			: Text(string.Empty);
	}

	private static async Task<IResult> AddFavouriteAsync(HttpContext context, WebAuthentication auth,
		CancellationToken ct)
	{
		return await GetFavouritesAsync(context, auth, ct);
	}

	private static async Task<IResult> RateAsync(HttpContext context, WebAuthentication auth, CancellationToken ct)
	{
		var query = context.Request.Query;
		return await auth.AuthenticateAsync(context, query["u"], query["p"], ct) is null
			? Results.Unauthorized()
			: Text(OsuWebReplies.NotRanked);
	}

	private static async Task<IResult> CommentAsync(HttpContext context, WebAuthentication auth, CancellationToken ct)
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

		return await auth.AuthenticateAsync(context, form["u"], form["p"], ct) is null
			? Results.Unauthorized()
			: Text(string.Empty);
	}

	private static string DirectLine(BeatmapsetRecord set)
	{
		var meta = set.Metadata;
		return
			$"{set.Id}.osz|{meta.Artist}|{meta.Title}|{meta.Creator}|{(int)set.Status}|10.0|{set.UpdatedAt:yyyy-MM-dd HH:mm:ss}|{set.Id}|0|0|0|0|0";
	}

	private static async Task<IResult> RegisterAsync(HttpContext context, SessionRegistry registry,
		ISender sender,
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
		var rules = (await sender.Send(new ReadSettings(), ct)).RateLimits.Registration;
		var ip = ClientAddress.Resolve(context);
		if (checkOnly &&
		    !limiter.TryAcquire($"register-check:{ip}", RegistrationChecksPerWindow, RegistrationCheckWindow))
			return Results.Json(
				new { form_error = new { user = new { username = new[] { "Too many registrations." } } } },
				statusCode: 429);
		RegistrationFailure? failure;
		try
		{
			failure = await registry.CheckRegistrationAsync(
				new RegisterAttempt(name, new Md5Hash(Encoding.UTF8.GetBytes(password))), ct);
		}
		catch (InvalidUserNameException)
		{
			return Results.Json(new { form_error = new { user = new { username = new[] { "InvalidName" } } } },
				statusCode: 400);
		}
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
			new RegisterAttempt(name, new Md5Hash(Encoding.UTF8.GetBytes(password))), ct);
		return error is null
			? Text("ok")
			: Results.Json(new { form_error = new { user = new { username = new[] { error.ToString() } } } },
				statusCode: 400);
	}

	private sealed record DifficultyRatingBody(
		[property: JsonPropertyName("beatmap_id")]
		int BeatmapId,
		[property: JsonPropertyName("mods")] int Mods,
		[property: JsonPropertyName("rating")] double Rating);

	private sealed record BeatmapInfoBody
	{
		public string[] Filenames { get; init; } = [];
	}
}

internal static class Md5Text
{
	public static bool TryParse(string? text, out Md5Hash hash)
	{
		hash = default;
		if (text is not { Length: 32 }) return false;
		try
		{
			hash = new Md5Hash(text);
			return true;
		}
		catch (ArgumentException)
		{
			return false;
		}
	}
}