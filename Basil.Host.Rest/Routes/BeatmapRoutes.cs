using Basil.Host.Shared.Http;
using Basil.Application.Beatmaps.Messages;
using Basil.Application.Beatmaps.Objects;
using Basil.Application.Beatmaps.Persistence;
using Basil.Application.Beatmaps.Storage;
using Basil.Application.Servers.Messages.Content;
using Basil.Application.Servers.Objects;
using Basil.Application.Shared.Paging;
using Basil.Application.Shared.Settings;
using Basil.Domain.Beatmaps;
using Basil.Gateway.Limits;
using Basil.Host.Rest.Http;
using Basil.Host.Shared.Views;
using Mediator;

namespace Basil.Host.Rest.Routes;

internal static class BeatmapRoutes
{
	private const string OpaqueType = "application/octet-stream";

	private const string UploadRoute = "POST /v1/beatmapsets";

	private static readonly Dictionary<BeatmapsetFileType, Dictionary<string, string>> AllowedTypes = new()
	{
		[BeatmapsetFileType.Background] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
			{ [".jpg"] = "image/jpeg", [".jpeg"] = "image/jpeg", [".png"] = "image/png" },
		[BeatmapsetFileType.Audio] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
			{ [".mp3"] = "audio/mpeg", [".ogg"] = "audio/ogg", [".wav"] = "audio/wav" },
		[BeatmapsetFileType.Preview] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
			{ [".mp3"] = "audio/mpeg" },
		[BeatmapsetFileType.Video] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
			{ [".mp4"] = "video/mp4", [".avi"] = "video/x-msvideo", [".flv"] = "video/x-flv", [".webm"] = "video/webm" }
	};

	public static void MapBeatmaps(this RouteGroupBuilder v1)
	{
		var sets = v1.MapGroup("/beatmapsets").WithTags("Beatmapsets");
		var maps = v1.MapGroup("/beatmaps").WithTags("Beatmaps");

		sets.MapGet("/", SearchAsync).Admitted(WorkClass.Listing).RequireToken().ReturnsList<SetView>().WithSummary("Search beatmapsets.")
			.WithDescription(
				"`query` uses the beatmap search syntax. Hidden sets are listed only for those who manage them.");
		sets.MapGet("/{id:int}", GetSetAsync).Returns<SetDetailView>().WithSummary("Get a beatmapset.")
			.WithDescription("Includes its beatmaps.");
		maps.MapGet("/{id:int}", GetBeatmapAsync).Returns<BeatmapView>().WithSummary("Get a beatmap.")
			.WithDescription("Includes its beatmapset.");
		sets.MapPost("/", UploadAsync).Admitted(WorkClass.Upload).RequireToken().DisableAntiforgery()
			.Returns<SetDetailView>(201).WithSummary("Upload a beatmapset.")
			.WithDescription("""
			                 The multipart form carries the `.osz` archive in `file`. The content of a set is never replaced: a set that already exists is refused with 409, and an archive that is not a readable beatmapset with 422.

			                 An archive over the configured size is refused with 413 and the error code `tooLarge` when its size is declared, and by closing the connection otherwise; content that is not a ZIP archive closes the connection.

			                 While the beatmapsets area is locked only managers can upload.
			                 """);
		sets.MapDelete("/{id:int}", DeleteAsync).RequireToken().WithSummary("Delete a beatmapset.")
			.WithDescription("Needs the Tournament privilege; a caller without it is refused with 403 whether or not the set exists. A set that does not exist is answered with 404.");
		sets.MapPatch("/{id:int}", PatchAsync).RequireToken()
			.Takes<PatchSetRequest>().Returns<SetView>().WithSummary("Update a beatmapset.")
			.WithDescription(
				"Changes the status, the visibility or the lock of the set; its content never changes. Changing the status can change how clients submit scores and replays; use a status with a leaderboard (ranked, approved, qualified, loved) so those functions keep working.");

		sets.MapGet("/{id:int}/audiopreview", (HttpContext c, int id, ISender s) =>
				SetFileAsync(c, id, s, BeatmapsetFileType.Preview))
			.WithSummary("Download the audio preview of a beatmapset.")
			.WithDescription("The answer is the preview clip of the set as audio, and it supports ranges. A set that does not exist or has no preview is answered with 404.");
		sets.MapGet("/{id:int}/storyboard", (HttpContext c, int id, ISender s) =>
				SetFileAsync(c, id, s, BeatmapsetFileType.Storyboard)).RequireToken()
			.WithSummary("Download the storyboard of a beatmapset.")
			.WithDescription("The answer is the storyboard file of the set. A set that does not exist or has no storyboard is answered with 404.");
		sets.MapGet("/{id:int}/download", DownloadAsync).RequireToken()
			.WithSummary("Download a beatmapset.")
			.WithDescription("The archive is an `.osz` file; `noVideo=1` leaves the video out of it.");
		foreach (var (name, token) in new[] { ("audio", true), ("video", true), ("background", false) })
		{
			var route = sets.MapGet("/{id:int}/" + name,
					(HttpContext c, int id, ISender s) => RedirectToLowestAsync(c, id, name, s))
				.WithSummary($"Download the {name} of a beatmapset.")
				.WithDescription(
					$"Redirects with 307 to the {name} of the lowest beatmap of the set; 404 when the set has no beatmaps.");
			if (token) route.RequireToken();
		}

		AssetRoute(maps, "audio", BeatmapsetFileType.Audio, true);
		AssetRoute(maps, "video", BeatmapsetFileType.Video, true);
		AssetRoute(maps, "background", BeatmapsetFileType.Background, false);
	}

	private static void AssetRoute(RouteGroupBuilder maps, string name, BeatmapsetFileType type, bool token)
	{
		var route = maps.MapGet("/{id:int}/" + name, async (HttpContext c, int id, ISender sender) =>
			Serve(c, await sender.Send(new OpenBeatmapFile(c.ConnectionOrNull(), id, type), c.RequestAborted),
				type, $"No such beatmap, or it has no {name}.")).WithSummary($"Download the {name} of a beatmap.")
			.WithDescription($"The answer is the {name} file of the beatmap, and it supports ranges. A beatmap that does not exist or has no {name} is answered with 404.");
		if (token) route.RequireToken();
	}

	private static IResult Serve(HttpContext context, BeatmapFile? file, BeatmapsetFileType type, string missing)
	{
		if (file is null) return Api.NotFound(missing);

		// A file name comes from an uploaded archive, so it never decides the type beyond the allowlist of its kind.
		var contentType = AllowedTypes.TryGetValue(type, out var allowed) &&
		                  allowed.TryGetValue(Path.GetExtension(file.Name), out var known)
			? known
			: OpaqueType;
		context.Response.Headers.XContentTypeOptions = "nosniff";
		return StoredFile.Send(context, file.Content, contentType, enableRangeProcessing: true);
	}

	private static IResult NoSniff(HttpContext context, IResult result)
	{
		context.Response.Headers.XContentTypeOptions = "nosniff";
		return result;
	}

	private static async Task<IResult> SearchAsync(HttpContext context, string? query, int? offset, int? limit,
		ISender sender)
	{
		var (page, error) = Targets.Paging(offset, limit);
		if (error is not null) return error;

		var found = await sender.Send(new SearchBeatmapsets(context.Session(),
			BeatmapQuery.Parse(query ?? string.Empty), page!), context.RequestAborted);
		return Api.Paged(new Page<SetView>([.. found.Items.Select(SetView.Of)], found.HasNext), page!);
	}

	private static async Task<IResult> GetSetAsync(HttpContext context, int id, ISender sender)
	{
		var detail = await sender.Send(new GetBeatmapset(context.ConnectionOrNull(), id), context.RequestAborted);
		return detail is null
			? Api.NotFound("No such beatmapset.")
			: Api.Ok(new SetDetailView(SetView.Of(detail.Set),
				[.. detail.Beatmaps.Select(b => BeatmapView.Of(b, false))]));
	}

	private static async Task<IResult> GetBeatmapAsync(HttpContext context, int id, ISender sender)
	{
		var map = await sender.Send(new GetBeatmap(context.ConnectionOrNull(), id), context.RequestAborted);
		return map is null ? Api.NotFound("No such beatmap.") : Api.Ok(BeatmapView.Of(map, true));
	}

	private static async Task<IResult> UploadAsync(HttpContext context, ISender sender)
	{
		if (!context.Request.HasFormContentType) return Api.Invalid("A multipart form with a file field is required.");

		var ct = context.RequestAborted;
		var session = context.Session();
		var limit = (await sender.Send(new ReadSettings(), ct)).Uploads.OszMaxSize;
		if (!UploadRequest.Accept(context, limit))
			return Api.Error(StatusCodes.Status413PayloadTooLarge, "tooLarge", "The archive is too large.");

		var early = await sender.Send(new MayUploadBeatmapset(session), ct);
		if (early != ServerActionResult.Ok) return early.Refusal();

		return await UploadRequest.GuardAsync(context, UploadRoute, $"user {session.Record.Id}", async () =>
		{
			await using var form = await UploadForm.ReadAsync(context, "file", UploadSignature.Archive, limit, [],
				false, ct);
			if (form.File is null) return Api.Invalid("The file field is missing.");

			var (result, import) = await sender.Send(new UploadBeatmapset(session, form.File), ct);
			if (result != ServerActionResult.Ok) return result.Refusal();

			return import!.Failure switch
			{
				null => Api.Created(new SetDetailView(SetView.Of(import.Set!),
					[.. import.Beatmaps.Select(b => BeatmapView.Of(b, false))])),
				BeatmapsetImportFailure.AlreadyExists => Api.Error(StatusCodes.Status409Conflict, "alreadyExists",
					"The beatmapset already exists."),
				BeatmapsetImportFailure.Locked => Api.Error(StatusCodes.Status409Conflict, "locked",
					"The beatmapset is locked."),
				BeatmapsetImportFailure.RulesetsMissing => Api.Error(StatusCodes.Status503ServiceUnavailable,
					"rulesetsMissing", "The osu! calculation stack is not installed. Install it with the ruleset update first."),
				_ => Api.Invalid("The archive is not a readable beatmapset.", "unreadable")
			};
		});
	}

	private static async Task<IResult> DeleteAsync(HttpContext context, int id, ISender sender)
	{
		return (await sender.Send(new DeleteBeatmapset(context.Session(), id), context.RequestAborted)).Answer();
	}

	private static async Task<IResult> PatchAsync(HttpContext context, int id, ISender sender)
	{
		var (request, refused) = await context.ReadBodyAsync<PatchSetRequest>();
		if (refused is not null) return refused;

		if (request is { Status: null, Visible: null, Locked: null })
			return Api.Invalid("Send at least one field to change.");
		if (request!.Status is { } status && !Enum.IsDefined(status)) return Api.Invalid("status is not valid.");

		var result = await sender.Send(
			new PatchBeatmapset(context.Session(), id, request.Status, request.Visible, request.Locked),
			context.RequestAborted);
		if (result != BeatmapsetEditResult.Ok) return result.Refusal();

		var detail = await sender.Send(new GetBeatmapset(context.Session(), id), context.RequestAborted);
		return detail is null
			? Api.NotFound("No such beatmapset.")
			: Api.Ok(SetView.Of(detail.Set), "Updated successfully");
	}

	private static async Task<IResult> SetFileAsync(HttpContext context, int id, ISender sender,
		BeatmapsetFileType type)
	{
		return Serve(context,
			await sender.Send(new OpenBeatmapsetFile(context.ConnectionOrNull(), id, type), context.RequestAborted),
			type, $"No such beatmapset, or it has no {type.ToString().ToLowerInvariant()}.");
	}

	private static async Task<IResult> DownloadAsync(HttpContext context, int id, ISender sender, string? noVideo)
	{
		var leavesVideoOut = noVideo is not null &&
		                     (noVideo == "1" || (bool.TryParse(noVideo, out var parsed) && parsed));
		var archive = await sender.Send(new OpenBeatmapsetArchive(context.ConnectionOrNull(), id, !leavesVideoOut),
			context.RequestAborted);
		return archive is null
			? Api.NotFound("No such beatmapset, or it has no archive.")
			: NoSniff(context, StoredFile.Send(context, archive, "application/x-osu-beatmap-archive", $"{id}.osz"));
	}

	private static async Task<IResult> RedirectToLowestAsync(HttpContext context, int id, string name,
		ISender sender)
	{
		var detail = await sender.Send(new GetBeatmapset(context.ConnectionOrNull(), id), context.RequestAborted);
		if (detail is null) return Api.NotFound("No such beatmapset.");

		return detail.Beatmaps.Count == 0
			? Api.NotFound("The beatmapset has no beatmaps.")
			: Results.Redirect($"/v1/beatmaps/{detail.Beatmaps[0].Id}/{name}", false, true);
	}

	public sealed record PatchSetRequest(BeatmapsetStatus? Status, bool? Visible, bool? Locked);
}