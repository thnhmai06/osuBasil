using System.Security.Cryptography;
using System.Text;
using Basil.Application.Abstractions.Beatmaps;
using Basil.Application.Abstractions.Scores;
using Basil.Application.Abstractions.Users;
using Basil.Application.Configurations;
using Basil.Application.Packets;
using Basil.Application.Services.Anticheat;
using Basil.Application.Services.Authentication;
using Basil.Application.Services.Beatmaps;
using Basil.Application.Services.Scores;
using Basil.Application.Sessions;
using Basil.Domain.Beatmaps;
using Basil.Domain.Login;
using Basil.Domain.Scores;
using Basil.Domain.Users;
using Basil.Infrastructure.Beatmaps;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Basil.Web.Routing.Bancho;

/// <summary>
///     Dedicated <c>ILogger&lt;T&gt;</c> category marker, because <see cref="OsuWebRoutes" /> is static and can't be
///     a type argument.
/// </summary>
// ReSharper disable once ClassNeverInstantiated.Global
internal sealed class OsuWebRoutesLog;

/// <summary>
///     Registers the `osu.{domain}` host's `/web/*.php` endpoints: leaderboard status, osu!direct
///     search and set lookup, beatmapset download, .osu file fetch, score submission, replay download,
///     anticheat flag receiver, seasonal backgrounds, client stubs, and in-game registration.
/// </summary>
internal static class OsuWebRoutes
{
	private static readonly string[] MissingUsernamePasswordMsg = ["Username and password are required."];

	private static readonly string[] InvalidAdminKeyMsg =
		["Invalid AdminKey. Please enter the AdminKey in the Email field to continue."];

	private static readonly string[] UsernameTakenMsg = ["Username already taken."];

	/// <summary>
	///     Registers the `osu.{domain}` host's routes.
	/// </summary>
	/// <param name="group">The `osu.{domain}` route group.</param>
	public static void MapOsuWebGroup(this RouteGroupBuilder group)
	{
		group.MapGet("/", () => Results.Redirect("https://github.com/thnhmai06/osuBasil", false))
			.WithGroupName("osuweb")
			.WithSummary("Homepage")
			.WithDescription("Redirects to the project's GitHub repository.")
			.WithTags("Homepage");

		group.MapGet("/health", () => "osu")
			.WithGroupName("osuweb")
			.WithSummary("Check server availability")
			.WithDescription("Returns the literal string \"osu\". The real osu! client never calls this " +
			                 "endpoint; it is a liveness probe for the `osu.` host.")
			.WithTags("Stubs");

		group.MapGet("/web/osu-osz2-getscores.php", async (
				[FromQuery(Name = "us")] string username,
				[FromQuery(Name = "ha")] string passwordMd5,
				[FromQuery(Name = "c")] string? checksum,
				[FromQuery(Name = "m")] int mode,
				[FromQuery(Name = "mods")] int mods,
				HttpContext context,
				CancellationToken cancellationToken) =>
			{
				var authentication = context.RequestServices.GetRequiredService<AuthenticationService>();
				var player =
					await authentication.AuthenticateOnlinePlayerAsync(username, passwordMd5, cancellationToken);
				if (player is null) return Results.StatusCode(StatusCodes.Status401Unauthorized);

				var gameMode = (GameMode)mode;
				var requestedMods = (Mods)mods;
				if (gameMode != player.Status.Mode || requestedMods != player.Status.Mods)
				{
					player.Status.Mode = gameMode;
					player.Status.Mods = requestedMods;

					if (!player.Restricted)
					{
						var sessionRegistry = context.RequestServices.GetRequiredService<IUserSessionRegistry>();
						var statsPacket = PacketBuilders.BuildUserStats(player);
						foreach (var other in sessionRegistry.All) other.Enqueue(statsPacket);
					}
				}

				Beatmap? beatmap = null;
				if (!string.IsNullOrEmpty(checksum))
				{
					var maps = context.RequestServices.GetRequiredService<IBeatmapRepository>();
					beatmap = await maps.FetchOneAsync(md5: checksum, cancellationToken: cancellationToken);
				}

				var status = beatmap is null ? BeatmapStatus.NotSubmitted : Beatmapset.Status;

				return Results.Text($"{(int)status}|false", "text/html", Encoding.UTF8);
			})
			.WithGroupName("osuweb")
			.WithSummary("Check a beatmap's leaderboard status")
			.WithDescription("Called by the client whenever the selected beatmap changes.\n\n" +
			                 "Authenticates with `us`/`ha` (username / password MD5) and updates the player's " +
			                 "mode and mods status when they change.\n\n" +
			                 "Returns `{rankedStatus}|false` for the map identified by `c` (its MD5 checksum).\n\n" +
			                 "This server does not support leaderboard browsing, so the score rows are always " +
			                 "empty.")
			.WithTags("Beatmaps");

		group.MapGet("/web/osu-search.php", async (
				[FromQuery(Name = "u")] string username,
				[FromQuery(Name = "h")] string passwordMd5,
				[FromQuery(Name = "q")] string query,
				[FromQuery(Name = "m")] int mode,
				[FromQuery(Name = "p")] int pageNum,
				HttpContext context,
				CancellationToken cancellationToken) =>
			{
				var authentication = context.RequestServices.GetRequiredService<AuthenticationService>();
				if (await authentication.AuthenticateOnlinePlayerAsync(username, passwordMd5, cancellationToken) is
				    null)
					return Results.StatusCode(StatusCodes.Status401Unauthorized);

				var searchService = context.RequestServices.GetRequiredService<DirectSearchService>();
				var request = new DirectSearchRequest(query, mode, pageNum);
				var response = await searchService.SearchFormattedAsync(request, cancellationToken);

				return Results.Text(response, "text/html", Encoding.UTF8);
			})
			.WithGroupName("osuweb")
			.WithSummary("Search beatmaps (osu!direct)")
			.WithDescription("Backs the in-game osu!direct search panel. Searches the configured mirror catalog " +
			                 "instead of local storage when a search mirror is configured, falling back to local " +
			                 "search if the mirror is unreachable; otherwise searches local storage only.\n\n" +
			                 "Query parameters:\n" +
			                 "* `q` — free-text query\n" +
			                 "* `m` — game-mode filter\n" +
			                 "* `p` — zero-based page number\n\n" +
			                 "The response uses osu!'s pipe/newline wire format, not JSON.")
			.WithTags("Beatmaps");

		// "s"/"b"/"c" are all optional, but exactly one is expected per request; when all three
		// are absent, an empty body is returned.
		group.MapGet("/web/osu-search-set.php", async (
				[FromQuery(Name = "u")] string username,
				[FromQuery(Name = "h")] string passwordMd5,
				[FromQuery(Name = "s")] int? mapSetId,
				[FromQuery(Name = "b")] int? mapId,
				[FromQuery(Name = "c")] string? checksum,
				HttpContext context,
				CancellationToken cancellationToken) =>
			{
				var authentication = context.RequestServices.GetRequiredService<AuthenticationService>();
				if (await authentication.AuthenticateOnlinePlayerAsync(username, passwordMd5, cancellationToken) is
				    null)
					return Results.StatusCode(StatusCodes.Status401Unauthorized);

				if (mapSetId is null && mapId is null && checksum is null)
					return Results.Text("", "text/html", Encoding.UTF8);

				var maps = context.RequestServices.GetRequiredService<IBeatmapRepository>();
				var bmapSet =
					await maps.FetchOneAsync(mapId, checksum, setId: mapSetId,
						cancellationToken: cancellationToken);

				return Results.Text(DirectSearchService.FormatSet(bmapSet), "text/html", Encoding.UTF8);
			})
			.WithGroupName("osuweb")
			.WithSummary("Retrieve one beatmap set")
			.WithDescription("Provide exactly one of the following:\n" +
			                 "* `s` — beatmapset id\n" +
			                 "* `b` — a beatmap id within the set\n" +
			                 "* `c` — a beatmap's MD5 checksum\n\n" +
			                 "Returns an empty body if the set cannot be resolved or none of the parameters is " +
			                 "present.\n\n" +
			                 "The response is a single pipe-delimited set-info line, not JSON.")
			.WithTags("Beatmaps");

		group.MapGet("/d/{mapSetId}",
				async (string mapSetId, HttpContext context, CancellationToken cancellationToken) =>
				{
					const char noVideoSuffix = 'n';
					var noVideo = mapSetId.EndsWith(noVideoSuffix);
					var rawSetId = noVideo ? mapSetId[..^1] : mapSetId;

					if (int.TryParse(rawSetId, out var setId))
					{
						var maps = context.RequestServices.GetRequiredService<IBeatmapRepository>();
						var storage = context.RequestServices.GetRequiredService<IOptions<StorageOptions>>().Value;
						var osz = await BanchoHostGroups.BuildBeatmapsetArchiveAsync(maps, storage, setId, noVideo,
							cancellationToken);
						if (osz is not null)
							return Results.File(osz.Value.Bytes, ContentTypes.Resolve(osz.Value.FileName),
								osz.Value.FileName);
					}

					var mirrorOptions = context.RequestServices.GetRequiredService<IOptions<MirrorOptions>>().Value;
					if (!mirrorOptions.IsOnlineMode)
						return Results.Text("Beatmap downloads are not available on this server.", "text/html",
							Encoding.UTF8);

					// A locally-synthesized id (no genuine ppy id) can never resolve on a ppy-id-keyed
					// mirror, regardless of whether Basil itself has ever seen this exact id before.
					if (int.TryParse(rawSetId, out var parsedSetId) && parsedSetId >= Beatmap.LocalIdFloor)
						return Results.Problem(
							"This endpoint is not available while the server runs in online mirror mode.",
							statusCode: StatusCodes.Status503ServiceUnavailable);

					const int noVideoQueryValue = 0;
					const int withVideoQueryValue = 1;
					var query = $"{rawSetId}?n={(noVideo ? noVideoQueryValue : withVideoQueryValue)}";

					return Results.Redirect($"{mirrorOptions.DownloadEndpoint}/{query}", true);
				})
			.WithGroupName("osuweb")
			.WithSummary("Download a beatmapset (osu!direct)")
			.WithDescription("The in-game download button. `{mapSetId}` may carry a trailing `n` to request " +
			                 "a no-video archive.\n\n" +
			                 "If the set is available on this server, a fresh `.osz` is built and returned as " +
			                 "`application/x-osu-beatmap-archive`. Otherwise, when the server runs in online " +
			                 "mirror mode, the request redirects to the configured mirror (`503` for a " +
			                 "locally-authored set with no genuine ppy id to redirect with), or returns a " +
			                 "plain-text \"not available\" message when no mirror is configured.\n\n" +
			                 "Prefer `GET /beatmapsets/{id}/download` on the Basil API for external tooling; this " +
			                 "route exists for the in-game client.")
			.WithTags("Beatmaps");

		group.MapGet("/web/beatmaps/{mapFilename}", async (string mapFilename, HttpContext context,
				CancellationToken cancellationToken) =>
			{
				var maps = context.RequestServices.GetRequiredService<IBeatmapRepository>();
				var bmap = await maps.FetchOneAsync(filename: mapFilename, cancellationToken: cancellationToken);
				if (bmap is null) return Results.NotFound();

				var storage = context.RequestServices.GetRequiredService<IOptions<StorageOptions>>().Value;
				var osuPath = BeatmapIngestionService.OsuFilePath(storage, bmap);
				return File.Exists(osuPath)
					? Results.File(osuPath, ContentTypes.Resolve(osuPath))
					: Results.NotFound();
			})
			.WithGroupName("osuweb")
			.WithSummary("Download a beatmap's .osu file")
			.WithDescription("Called by the client when it detects its local copy of a difficulty is stale.\n\n" +
			                 "`{mapFilename}` is the exact filename recorded for this difficulty, not a beatmap id.\n\n" +
			                 "Returns the file as `application/x-osu-beatmap`.\n\n" +
			                 "404 — No matching beatmap, or the file is missing.")
			.WithTags("Beatmaps");

		// The client reuses the "score" field name for both the base64 score-data string
		// and the replay file upload; ASP.NET Core's multipart parser already separates text parts
		// from file parts by name, so no manual form-field workaround is needed here. Unused
		// fields the client sends, but this handler never forwards into submission (fs/x/i) are
		// not bound at all.
		group.MapPost("/web/osu-submit-modular-selector.php",
				async (HttpContext context, CancellationToken cancellationToken) =>
				{
					if (!context.Request.HasFormContentType) return Results.Text("", "text/html", Encoding.UTF8);

					var form = await context.Request.ReadFormAsync(cancellationToken);
					var scoreDataB64 = form["score"].FirstOrDefault();
					if (scoreDataB64 is null) return Results.Text("", "text/html", Encoding.UTF8);

					byte[]? replayData = null;
					var replayFile = form.Files.GetFile("score");
					if (replayFile is not null)
					{
						using var replayStream = new MemoryStream();
						await replayFile.CopyToAsync(replayStream, cancellationToken);
						replayData = replayStream.ToArray();
					}

					var osuVersion = form["osuver"].FirstOrDefault() ?? "";
					var decryptor = context.RequestServices.GetRequiredService<IScoreDecryptor>();
					var (scoreDataFields, clientHash) = decryptor.Decrypt(
						scoreDataB64, form["s"].FirstOrDefault() ?? "", form["iv"].FirstOrDefault() ?? "",
						osuVersion);

					var useCase = context.RequestServices.GetRequiredService<ScoreSubmissionService>();
					var outcome = await useCase.SubmitAsync(
						new ScoreSubmissionRequest(
							scoreDataFields,
							form["pass"].FirstOrDefault() ?? "",
							osuVersion,
							clientHash,
							form["c1"].FirstOrDefault() ?? "",
							form["sbk"].FirstOrDefault(),
							form["bmk"].FirstOrDefault() ?? "",
							int.Parse(form["st"].FirstOrDefault() ?? "0"),
							int.Parse(form["ft"].FirstOrDefault() ?? "0"),
							replayData),
						cancellationToken);

					var domain = context.RequestServices.GetRequiredService<IOptions<ServerOptions>>().Value.Domain;
					var body = outcome.Code == ScoreSubmissionResultCode.Success
						? ScoreSubmissionResponseBuilder.BuildSuccess(outcome.Result!, domain)
						: ScoreSubmissionResponseBuilder.BuildError(outcome.Code);

					return Results.Text(body, "text/html", Encoding.UTF8);
				})
			.WithGroupName("osuweb")
			.WithSummary("Submit a completed play")
			.WithDescription("Multipart form submission the client sends after a play completes.\n\n" +
			                 "The `score` field carries both a base64-encoded, encrypted score payload and, as a " +
			                 "file part of the same name, the replay data. `s`, `iv`, and `osuver` hold the decryption " +
			                 "key material.\n\n" +
			                 "On success the score is stored and the response carries the post-score screen data.\n\n" +
			                 "The response is always `text/html` in osu!'s own grammar, even on failure.")
			.WithTags("Score Submission");

		// `mode` is accepted by the client but never actually used when fetching the replay
		// file, so it's not bound here either.
		group.MapGet("/web/osu-getreplay.php", async (
				[FromQuery(Name = "u")] string username,
				[FromQuery(Name = "h")] string passwordMd5,
				[FromQuery(Name = "c")] long scoreId,
				HttpContext context,
				CancellationToken cancellationToken) =>
			{
				var authentication = context.RequestServices.GetRequiredService<AuthenticationService>();
				var player =
					await authentication.AuthenticateOnlinePlayerAsync(username, passwordMd5, cancellationToken);
				if (player is null) return Results.StatusCode(StatusCodes.Status401Unauthorized);

				var replayService = context.RequestServices.GetRequiredService<ReplayService>();
				var result = await replayService.FetchReplayFileAsync(scoreId, cancellationToken);

				return result.Code == ReplayFetchResultCode.NotFound
					? Results.NotFound()
					: Results.Bytes(result.Data!, "application/x-osu-replay");
			})
			.WithGroupName("osuweb")
			.WithSummary("Download a replay")
			.WithDescription("The in-game \"View Replay\" action.\n\n" +
			                 "Authenticates with `u`/`h` (username / password MD5), then returns the `.osr` replay " +
			                 "for the score identified by `c` as `application/x-osu-replay`.\n\n" +
			                 "404 — The score has no stored replay.\n\n" +
			                 "Prefer `GET /scores/{scoreId}/replay` on the Basil API for external tooling; this " +
			                 "route requires client-style authentication.")
			.WithTags("Replays");

		group.MapPost("/web/osu-getbeatmapinfo.php", async (
				[FromQuery(Name = "u")] string username,
				[FromQuery(Name = "h")] string passwordMd5,
				HttpContext context,
				CancellationToken cancellationToken) =>
			{
				var authentication = context.RequestServices.GetRequiredService<AuthenticationService>();
				var player =
					await authentication.AuthenticateOnlinePlayerAsync(username, passwordMd5, cancellationToken);
				return player is not null
					? Results.Text("", "text/html", Encoding.UTF8)
					: Results.StatusCode(StatusCodes.Status401Unauthorized);
			})
			.WithGroupName("osuweb")
			.WithSummary("Retrieve per-map grades (stub)")
			.WithDescription("Always returns an empty body after authenticating the caller.\n\n" +
			                 "Per-map grade history, used by the real client's Song Select grade icons, is out of " +
			                 "scope: this server does not track a leaderboard to grade against.")
			.WithTags("Stubs");

		group.MapGet("/web/lastfm.php", async (
				[FromQuery(Name = "b")] string beatmapIdOrHiddenFlag,
				[FromQuery(Name = "us")] string username,
				[FromQuery(Name = "ha")] string passwordMd5,
				HttpContext context,
				CancellationToken cancellationToken) =>
			{
				var authentication = context.RequestServices.GetRequiredService<AuthenticationService>();
				var player =
					await authentication.AuthenticateOnlinePlayerAsync(username, passwordMd5, cancellationToken);
				if (player is null) return Results.StatusCode(StatusCodes.Status401Unauthorized);

				var clientIntegrity = context.RequestServices.GetRequiredService<ClientIntegrityService>();
				var result =
					await clientIntegrity.HandleLastFmFlagsAsync(player, beatmapIdOrHiddenFlag);

				return result == ClientIntegrityResult.StopSending
					? Results.Text("-3", "text/html", Encoding.UTF8)
					: Results.Text("", "text/html", Encoding.UTF8);
			})
			.WithGroupName("osuweb")
			.WithSummary("Submit anticheat flags")
			.WithDescription("The osu! client's cheat-tool detection reports land here. `b` is a beatmap id, " +
			                 "or a flag string when the client is flagging something other than a beatmap.\n\n" +
			                 "Flags are logged for manual review only; there is no automatic restriction " +
			                 "pipeline.\n\n" +
			                 "Returns `-3` to tell the client to stop sending further flags for this session, or an " +
			                 "empty body otherwise.")
			.WithTags("Anticheat");

		group.MapGet("/web/osu-markasread.php", async (
				[FromQuery(Name = "u")] string username,
				[FromQuery(Name = "h")] string passwordMd5,
				HttpContext context,
				CancellationToken cancellationToken) =>
			{
				var authentication = context.RequestServices.GetRequiredService<AuthenticationService>();
				var player =
					await authentication.AuthenticateOnlinePlayerAsync(username, passwordMd5, cancellationToken);
				return player is null
					? Results.StatusCode(StatusCodes.Status401Unauthorized)
					: Results.Text("", "text/html", Encoding.UTF8);
			})
			.WithGroupName("osuweb")
			.WithSummary("Mark mail as read (no-op)")
			.WithDescription("Always returns an empty body after authenticating the caller.\n\n" +
			                 "This server has no offline-mail persistence; chat is online only, so there is " +
			                 "nothing to mark as read. The route exists only so the client does not treat a missing " +
			                 "endpoint as a connectivity failure.")
			.WithTags("Mail");

		group.MapGet("/web/osu-getseasonal.php", (HttpContext context) =>
			{
				var storage = context.RequestServices.GetRequiredService<IOptions<StorageOptions>>().Value;
				var domain = context.RequestServices.GetRequiredService<IOptions<ServerOptions>>().Value.Domain;
				Directory.CreateDirectory(storage.SeasonalsPath);

				var files = Directory.EnumerateFiles(storage.SeasonalsPath)
					.Select(path => $"https://osu.{domain}/seasonal/{Path.GetFileName(path)}")
					.ToArray();
				return Results.Json(files);
			})
			.WithGroupName("osuweb")
			.WithSummary("List seasonal backgrounds")
			.WithDescription("Returns a JSON array of full URLs for the login-screen seasonal backgrounds, " +
			                 "one per image file added to this server, each pointing at `GET /seasonal/{fileName}` " +
			                 "on this same host.\n\n" +
			                 "Unlike most routes on this host, the response really is JSON, matching what the " +
			                 "osu! client expects here.")
			.WithTags("Seasonal Backgrounds");

		// Serves the files listed by osu-getseasonal.php above.
		group.MapGet("/seasonal/{fileName}", (string fileName, HttpContext context) =>
			{
				var storage = context.RequestServices.GetRequiredService<IOptions<StorageOptions>>().Value;
				// Path.GetFileName strips any directory component the client could smuggle in
				// (e.g. "../../appsettings.json") before it ever reaches Path.Combine.
				var path = Path.Combine(storage.SeasonalsPath, Path.GetFileName(fileName));
				return !File.Exists(path) ? Results.NotFound() : Results.File(path, ContentTypes.Resolve(path));
			})
			.WithGroupName("osuweb")
			.WithSummary("Download a seasonal background")
			.WithDescription("`{fileName}` is one of the filenames returned by " +
			                 "`GET /web/osu-getseasonal.php`.\n\n" +
			                 "The response Content-Type is inferred from the extension (png/jpg/gif; anything " +
			                 "else is returned as `application/octet-stream`).\n\n" +
			                 "404 — The file does not exist.")
			.WithTags("Seasonal Backgrounds");

		// Deliberately unauthenticated: this is called before a bancho session exists, so there
		// is no credential to check against.
		group.MapGet("/web/bancho_connect.php", () => Results.Text("", "text/html", Encoding.UTF8))
			.WithGroupName("osuweb")
			.WithSummary("Check client connectivity")
			.WithDescription("Always returns an empty body.\n\n" +
			                 "Called by the client before a bancho session exists, so it is deliberately " +
			                 "unauthenticated.")
			.WithTags("Stubs");

		group.MapGet("/web/check-updates.php", () => Results.Text("", "text/html", Encoding.UTF8))
			.WithGroupName("osuweb")
			.WithSummary("Check for client updates")
			.WithDescription("Always returns an empty body. This server does not manage or distribute " +
			                 "client updates.")
			.WithTags("Stubs");

		group.MapPost("/web/osu-screenshot.php", () =>
				Results.Text("Screenshots are not available on this server.", "text/html", Encoding.UTF8,
					StatusCodes.Status400BadRequest))
			.WithGroupName("osuweb")
			.WithSummary("Upload a screenshot (not supported)")
			.WithDescription("Always returns 400 with an explanatory message. Screenshot hosting is out of " +
			                 "scope for this server.")
			.WithTags("Stubs");

		group.MapGet("/web/osu-getfavourites.php", () => Results.Text("", "text/html", Encoding.UTF8))
			.WithGroupName("osuweb")
			.WithSummary("List favourited beatmaps (stub)")
			.WithDescription("Always returns an empty body. Favourites are out of scope for this server.")
			.WithTags("Stubs");

		group.MapGet("/web/osu-addfavourite.php", () => Results.Text("", "text/html", Encoding.UTF8))
			.WithGroupName("osuweb")
			.WithSummary("Add a favourited beatmap (stub)")
			.WithDescription("Always returns an empty body. Favourites are out of scope for this server.")
			.WithTags("Stubs");

		// "not ranked" is a real response code the Python source itself sends (BeatmapRatingResultCode.NOT_RANKED),
		// reused here instead of an ad-hoc string.
		group.MapGet("/web/osu-rate.php", () => Results.Text("not ranked", "text/html", Encoding.UTF8))
			.WithGroupName("osuweb")
			.WithSummary("Rate a beatmap (not supported)")
			.WithDescription("Always returns the literal `not ranked` response osu! uses for beatmaps that " +
			                 "cannot be rated. Beatmap rating is out of scope for this server.")
			.WithTags("Stubs");

		group.MapPost("/web/osu-comment.php", () => Results.Text("", "text/html", Encoding.UTF8))
			.WithGroupName("osuweb")
			.WithSummary("Post a replay comment (stub)")
			.WithDescription("Always returns an empty body. In-replay comments are out of scope for this " +
			                 "server.")
			.WithTags("Stubs");

		// In-game registration: the client sends user[username], user[user_email], user[password],
		// plus a `check` field. "0" is the real submittion; any other value is a live-validation POST
		// fired while the user is still filling the form (one per field, as they tab through).
		// Only "0" may create the account. Other values run every validation below and report errors
		// the same way, but stop short of CreateAsync, so filling in earlier fields doesn't already
		// register the account before the user reaches submittion.
		// The Email field must contain the server's admin key (see AdminKeyService), unless the
		// server is in bypass mode (no key configured), in which case registration is open to
		// anyone. Registered users get default privileges (Unrestricted | Verified | Supporter).
		group.MapPost("/users", async (HttpContext context, IUserRepository users,
				IPasswordHasher passwordHasher, AdminKeyService adminKeyService,
				ILogger<OsuWebRoutesLog> logger, CancellationToken cancellationToken) =>
			{
				var username = context.Request.Form["user[username]"].FirstOrDefault();
				var email = context.Request.Form["user[user_email]"].FirstOrDefault();
				var password = context.Request.Form["user[password]"].FirstOrDefault();
				var check = context.Request.Form["check"].FirstOrDefault();

				if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
					return Results.Json(new { form_error = new { user = new { email = MissingUsernamePasswordMsg } } },
						statusCode: StatusCodes.Status400BadRequest);

				if (!User.ValidateUsername(username, out var usernameError))
					return Results.Json(new { form_error = new { user = new { username = new[] { usernameError } } } },
						statusCode: StatusCodes.Status400BadRequest);

				var isBypass = await adminKeyService.IsBypassAsync(cancellationToken);

				if (!isBypass && (string.IsNullOrEmpty(email) || !await adminKeyService.VerifyAsync(email, cancellationToken)))
				{
					// Only log on the real submittion (check=="0"). Per-field live-validation POSTs fire on
					// every blur while the form is still being filled in, and would otherwise log this
					// branch many times per genuine registration attempt.
					if (check == "0")
						logger.LogInformation("Registration rejected: invalid AdminKey. Username={Username}", username);
					return Results.Json(new { form_error = new { user = new { email = InvalidAdminKeyMsg } } },
						statusCode: StatusCodes.Status400BadRequest);
				}

				if (await users.FetchByNameAsync(username, cancellationToken) is not null)
					return Results.Json(new { form_error = new { user = new { username = UsernameTakenMsg } } },
						statusCode: StatusCodes.Status409Conflict);

				if (check != "0") return Results.Text("");

				var passwordMd5 = Convert.ToHexStringLower(MD5.HashData(Encoding.UTF8.GetBytes(password)));
				var pwBcrypt = passwordHasher.Hash(Encoding.UTF8.GetBytes(passwordMd5));
				var user = await users.CreateAsync(
					username, pwBcrypt, Country.Xx, cancellationToken: cancellationToken);
				if (user is null)
					return Results.Json(new { form_error = new { user = new { username = UsernameTakenMsg } } },
						statusCode: StatusCodes.Status409Conflict);

				logger.LogInformation("User registered in-game: NewUserId={NewUserId} Username={Username}",
					user.Id, user.Name);
				return Results.Json(new { id = user.Id, name = user.Name });
			})
			.WithGroupName("osuweb")
			.WithSummary("Register a player account")
			.WithDescription("Form fields: `user[username]`, `user[user_email]`, `user[password]`, and " +
			                 "`check`.\n\n" +
			                 "`check` is `\"0\"` for the real submit. Any other value is a live per-field " +
			                 "validation POST the client fires while the form is still being filled in; it runs the " +
			                 "same validation but does not create the account.\n\n" +
			                 "The `user_email` field must match the server's admin key, which gates registration. " +
			                 "Registration is open to anyone while the server is in bypass mode (no admin key " +
			                 "configured).\n\n" +
			                 "New accounts get default privileges (Unrestricted | Verified | Supporter).")
			.WithTags("Registration");

		group.MapPost("/difficulty-rating", async (
				[FromQuery(Name = "b")] int? beatmapId,
				HttpContext context,
				CancellationToken cancellationToken,
				[FromQuery(Name = "mods")] int mods = 0) =>
			{
				if (beatmapId is null)
					return Results.Text("Difficulty rating requires a beatmap id (?b=).", "text/html",
						Encoding.UTF8);

				var maps = context.RequestServices.GetRequiredService<IBeatmapRepository>();
				var bmap = await maps.FetchOneAsync(beatmapId, cancellationToken: cancellationToken);
				if (bmap is null) return Results.NotFound();

				var storage = context.RequestServices.GetRequiredService<IOptions<StorageOptions>>().Value;
				var osuPath = BeatmapIngestionService.OsuFilePath(storage, bmap);
				if (!File.Exists(osuPath))
					return Results.Text("Beatmap file not available.", "text/html", Encoding.UTF8);

				var requestedMods = (Mods)mods;
				double stars;
				if (requestedMods == Mods.NoMod && bmap.Difficulty.Sr > 0)
				{
					stars = bmap.Difficulty.Sr;
				}
				else
				{
					var calculator = context.RequestServices.GetRequiredService<IOsuCalculator>();
					stars = calculator.Analyze(osuPath, bmap.Difficulty.Mode, requestedMods).Difficulty.Sr;
					if (requestedMods == Mods.NoMod)
						await maps.UpdateDiffAsync(bmap.Id, stars, cancellationToken);
				}

				return Results.Json(new { beatmap_id = bmap.Id, mods = (int)requestedMods, rating = stars });
			})
			.WithGroupName("osuweb")
			.WithSummary("Retrieve a beatmap's star rating")
			.WithDescription("The real osu! client normally opens a difficulty-rating webpage in the system " +
			                 "browser for this action; this server has no such page and computes the star rating " +
			                 "directly.\n\n" +
			                 "Query parameters:\n" +
			                 "* `b` (required) — beatmap id\n" +
			                 "* `mods` (default 0) — mod bitmask\n\n" +
			                 "Response: `{beatmap_id, mods, rating}` in snake_case.")
			.WithTags("Beatmaps");
	}
}