using Basil.Application.Beatmaps.Messages;
using Basil.Application.Beatmaps.Persistence;
using Basil.Application.Beatmaps.Storage;
using Basil.Application.Common;
using Basil.Application.Content.Persistence;
using Basil.Application.Servers.Messages;
using Basil.Domain.Beatmaps;
using Basil.Domain.Users;
using Basil.Gateway;
using Basil.Host.Rest.Http;
using Basil.Host.Shared.Views;
using Mediator;

namespace Basil.Host.Rest.Routes;

/// <summary>Beatmapsets, beatmaps and their files.</summary>
internal static class BeatmapRoutes
{
	private const string OpaqueType = "application/octet-stream";

	/// <summary>The content types each kind of file may be served as; anything else is an opaque download.</summary>
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

		sets.MapGet("/", SearchAsync).Admitted(WorkClass.Listing).RequireToken().WithSummary("Search beatmapsets.")
			.WithDescription(
				"`query` uses the beatmap search syntax. Hidden sets are listed only for those who manage them. Paged with `offset` and `limit`.");
		sets.MapGet("/{id:int}", GetSetAsync).WithSummary("Get a beatmapset with its beatmaps.");
		maps.MapGet("/{id:int}", GetBeatmapAsync).WithSummary("Get a beatmap with its beatmapset.");
		sets.MapPost("/", UploadAsync).Admitted(WorkClass.Upload).RequireToken().DisableAntiforgery()
			.WithSummary("Upload a beatmapset as an .osz archive in the `file` field.")
			.WithDescription(
				"A set that already exists is refused; the content of a set is never replaced. While the beatmapsets area is locked only managers can upload.");
		sets.MapDelete("/{id:int}", DeleteAsync).RequireToken().WithSummary("Delete a beatmapset.");
		sets.MapPatch("/{id:int}", PatchAsync).RequireToken()
			.WithSummary("Update the status, visibility or lock of a beatmapset.")
			.WithDescription(
				"Changing the status can change how clients submit scores and replays; use a status with a leaderboard (ranked, approved, qualified, loved) so those functions keep working.");

		sets.MapGet("/{id:int}/audiopreview", (HttpContext c, int id, ISender s) =>
				SetFileAsync(c, id, s, BeatmapsetFileType.Preview))
			.WithSummary("Get the preview clip of a beatmapset.");
		sets.MapGet("/{id:int}/storyboard", (HttpContext c, int id, ISender s) =>
			SetFileAsync(c, id, s, BeatmapsetFileType.Storyboard)).RequireToken()
			.WithSummary("Download the storyboard of a beatmapset.");
		sets.MapGet("/{id:int}/download", DownloadAsync).RequireToken()
			.WithSummary("Download a beatmapset as an .osz archive.")
			.WithDescription("`noVideo=1` leaves the video out.");
		foreach (var (name, token) in new[] { ("audio", true), ("video", true), ("background", false) })
		{
			var route = sets.MapGet("/{id:int}/" + name,
					(HttpContext c, int id, ISender s) => RedirectToLowestAsync(c, id, name, s))
				.WithSummary($"Get the {name} of the lowest beatmap of the set.");
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
				type, "No such file.")).WithSummary($"Download the {name} of a beatmap.");
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
		return Results.Stream(file.Content, contentType, enableRangeProcessing: true);
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

		var found = await sender.Send(new SearchBeatmapsets(context.Connection(),
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

	private static async Task<IResult> UploadAsync(HttpContext context, ISender sender, ISettingsRepository settings)
	{
		if (!context.Request.HasFormContentType) return Api.Invalid("A multipart form with a file field is required.");

		await UploadLimit.AllowAsync(context, settings);

		var form = await context.Request.ReadFormAsync(context.RequestAborted);
		if (form.Files.GetFile("file") is not { } file) return Api.Invalid("The file field is missing.");

		await using var stream = file.OpenReadStream();
		var (result, import) = await sender.Send(new UploadBeatmapset(context.Connection(), stream),
			context.RequestAborted);
		if (!result.IsOk) return result.Refusal();

		return import!.Failure switch
		{
			null => Api.Created(new SetDetailView(SetView.Of(import.Set!),
				[.. import.Beatmaps.Select(b => BeatmapView.Of(b, false))])),
			BeatmapsetImportFailure.AlreadyExists => Api.Error(StatusCodes.Status409Conflict, "alreadyExists",
				"The beatmapset already exists."),
			BeatmapsetImportFailure.Locked => Api.Error(StatusCodes.Status409Conflict, "locked",
				"The beatmapset is locked."),
			_ => Api.Invalid("The archive is not a readable beatmapset.", "unreadable")
		};
	}

	private static async Task<IResult> DeleteAsync(HttpContext context, int id, ISender sender)
	{
		return (await sender.Send(new DeleteBeatmapset(context.Connection(), id), context.RequestAborted)).Answer();
	}

	private static async Task<IResult> PatchAsync(HttpContext context, int id, PatchSetRequest request,
		ISender sender)
	{
		if (request.Status is { } status && !Enum.IsDefined(status)) return Api.Invalid("status is not valid.");

		var result = await sender.Send(
			new PatchBeatmapset(context.Connection(), id, request.Status, request.Visible, request.Locked),
			context.RequestAborted);
		if (!result.IsOk) return result.Refusal();

		var detail = await sender.Send(new GetBeatmapset(context.Connection(), id), context.RequestAborted);
		return detail is null
			? Api.NotFound("No such beatmapset.")
			: Api.Ok(SetView.Of(detail.Set), "Updated successfully");
	}

	private static async Task<IResult> SetFileAsync(HttpContext context, int id, ISender sender,
		BeatmapsetFileType type)
	{
		return Serve(context,
			await sender.Send(new OpenBeatmapsetFile(context.ConnectionOrNull(), id, type), context.RequestAborted),
			type, "No such file.");
	}

	private static async Task<IResult> DownloadAsync(HttpContext context, int id, ISender sender, string? noVideo)
	{
		var leavesVideoOut = noVideo is not null &&
		                     (noVideo == "1" || (bool.TryParse(noVideo, out var parsed) && parsed));
		var archive = await sender.Send(new OpenBeatmapsetArchive(context.ConnectionOrNull(), id, !leavesVideoOut),
			context.RequestAborted);
		return archive is null
			? Api.NotFound("No such file.")
			: NoSniff(context, Results.Stream(archive, "application/x-osu-beatmap-archive", $"{id}.osz"));
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

	/// <summary>The management properties of a set that can change; the content never changes.</summary>
	public sealed record PatchSetRequest(BeatmapsetStatus? Status, bool? Visible, bool? Locked);
}