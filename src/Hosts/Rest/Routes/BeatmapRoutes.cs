using Basil.Application.Beatmaps.Persistence;
using Basil.Application.Beatmaps.Storage;
using Basil.Application.Beatmaps.Capabilities;
using Basil.Application.Beatmaps.Objects;
using Basil.Application.Common;
using Basil.Application.Common.Common;
using Basil.Application.Servers.Objects;
using Basil.Domain.Beatmaps;
using Basil.Domain.Mechanics;
using Basil.Domain.Users;
using Basil.Host.Rest.Http;
using Basil.Host.Shared.Views;

namespace Basil.Host.Rest.Routes;

/// <summary>Beatmapsets, beatmaps and their files.</summary>
internal static class BeatmapRoutes
{
	/// <summary>The content types each kind of file may be served as; anything else is an opaque download.</summary>
	private static readonly Dictionary<BeatmapsetFileType, Dictionary<string, string>> AllowedTypes = new()
	{
		[BeatmapsetFileType.Background] = new(StringComparer.OrdinalIgnoreCase)
			{ [".jpg"] = "image/jpeg", [".jpeg"] = "image/jpeg", [".png"] = "image/png" },
		[BeatmapsetFileType.Audio] = new(StringComparer.OrdinalIgnoreCase)
			{ [".mp3"] = "audio/mpeg", [".ogg"] = "audio/ogg", [".wav"] = "audio/wav" },
		[BeatmapsetFileType.Preview] = new(StringComparer.OrdinalIgnoreCase) { [".mp3"] = "audio/mpeg" },
		[BeatmapsetFileType.Video] = new(StringComparer.OrdinalIgnoreCase)
			{ [".mp4"] = "video/mp4", [".avi"] = "video/x-msvideo", [".flv"] = "video/x-flv", [".webm"] = "video/webm" }
	};

	private const string OpaqueType = "application/octet-stream";

	/// <summary>The management properties of a set that can change; the content never changes.</summary>
	public sealed record PatchSetRequest(BeatmapsetStatus? Status, bool? Visible, bool? Locked);

	public static void MapBeatmaps(this RouteGroupBuilder v1)
	{
		var sets = v1.MapGroup("/beatmapsets").WithTags("Beatmapsets");
		var maps = v1.MapGroup("/beatmaps").WithTags("Beatmaps");

		sets.MapGet("/", SearchAsync).RequireToken().WithSummary("Search beatmapsets.")
			.WithDescription("`query` uses the beatmap search syntax. Hidden sets are listed only for those who manage them. Paged with `offset` and `limit`.");
		sets.MapGet("/{id:int}", GetSetAsync).WithSummary("Retrieve a beatmapset with its beatmaps.");
		maps.MapGet("/{id:int}", GetBeatmapAsync).WithSummary("Retrieve a beatmap with its beatmapset.");
		sets.MapPost("/", UploadAsync).RequireToken().DisableAntiforgery()
			.WithSummary("Upload a beatmapset as an .osz archive in the `file` field.")
			.WithDescription("A set that already exists is refused; the content of a set is never replaced. While the beatmapsets area is locked only managers can upload.");
		sets.MapDelete("/{id:int}", DeleteAsync).RequireToken().WithSummary("Delete a beatmapset.");
		sets.MapPatch("/{id:int}", PatchAsync).RequireToken().WithSummary("Change the status, visibility or lock of a beatmapset.")
			.WithDescription("Changing the status can change how clients submit scores and replays; use a status with a leaderboard (ranked, approved, qualified, loved) so those functions keep working.");

		sets.MapGet("/{id:int}/audiopreview", (HttpContext c, int id, IBeatmapsetRepository r, IBeatmapRepository b, IBeatmapsetStorage s) =>
			SetFileAsync(c, id, r, b, s, BeatmapsetFileType.Preview, m => m.Assets.AudioFile))
			.WithSummary("Play the preview clip of a beatmapset.");
		sets.MapGet("/{id:int}/storyboard", (HttpContext c, int id, IBeatmapsetRepository r, IBeatmapsetStorage s) =>
			StoryboardAsync(c, id, r, s)).RequireToken().WithSummary("Download the storyboard of a beatmapset.");
		sets.MapGet("/{id:int}/download", DownloadAsync).RequireToken()
			.WithSummary("Download a beatmapset as an .osz archive.")
			.WithDescription("`noVideo=1` leaves the video out.");
		foreach (var (name, token) in new[] { ("audio", true), ("video", true), ("background", false) })
		{
			var route = sets.MapGet("/{id:int}/" + name, (HttpContext c, int id, IBeatmapsetRepository r, IBeatmapRepository b) =>
				RedirectToLowestAsync(c, id, name, r, b)).WithSummary($"Redirect to the {name} of the lowest beatmap of the set.");
			if (token) route.RequireToken();
		}

		AssetRoute(maps, "audio", BeatmapsetFileType.Audio, m => m.Assets.AudioFile, true);
		AssetRoute(maps, "video", BeatmapsetFileType.Video, m => m.Assets.VideoFile, true);
		AssetRoute(maps, "background", BeatmapsetFileType.Background, m => m.Assets.BackgroundFile, false);
	}

	private static void AssetRoute(RouteGroupBuilder maps, string name, BeatmapsetFileType type,
		Func<BeatmapRecord, string?> file, bool token)
	{
		var route = maps.MapGet("/{id:int}/" + name, async (HttpContext c, int id, IBeatmapRepository beatmaps,
			IBeatmapsetStorage storage) =>
		{
			var beatmap = await beatmaps.GetAsync(id, c.RequestAborted);
			if (beatmap is null || !CanView(c, beatmap) || file(beatmap) is not { } fileName)
				return Api.NotFound("No such file.");

			return await Serve(c, storage, beatmap.Beatmapset, type, fileName);
		}).WithSummary($"Download the {name} of a beatmap.");
		if (token) route.RequireToken();
	}

	private static bool CanView(HttpContext context, BeatmapsetRecord set)
	{
		return set.Visible || MayViewHidden(context);
	}

	/// <summary>A beatmap is hidden when it or its set is; the same permission shows both.</summary>
	private static bool CanView(HttpContext context, BeatmapRecord map)
	{
		return map.IsVisible() || MayViewHidden(context);
	}

	private static bool MayViewHidden(HttpContext context)
	{
		var connection = context.ConnectionOrNull();
		var clock = context.RequestServices.GetRequiredService<TimeProvider>();
		return connection?.EffectivePermissions(clock.GetUtcNow()).Allows(UserPermissions.BeatmapsetManageViewHidden) ==
		       true;
	}

	private static async Task<IResult> Serve(HttpContext context, IBeatmapsetStorage storage, BeatmapsetRecord set,
		BeatmapsetFileType type, string name)
	{
		var stream = await storage.OpenAsync(set, type, name, context.RequestAborted);
		if (stream is null) return Api.NotFound("No such file.");

		// A file name comes from an uploaded archive, so it never decides the type beyond the allowlist of its kind.
		var contentType = AllowedTypes.TryGetValue(type, out var allowed) &&
		                  allowed.TryGetValue(Path.GetExtension(name), out var known)
			? known
			: OpaqueType;
		context.Response.Headers.XContentTypeOptions = "nosniff";
		return Results.Stream(stream, contentType, enableRangeProcessing: true);
	}

	private static IResult NoSniff(HttpContext context, IResult result)
	{
		context.Response.Headers.XContentTypeOptions = "nosniff";
		return result;
	}

	private static async Task<IResult> SearchAsync(HttpContext context, string? query, int? offset, int? limit,
		IBeatmapsetRepository repository, TimeProvider clock)
	{
		var (page, error) = Targets.Paging(offset, limit);
		if (error is not null) return error;

		var effective = context.Connection().EffectivePermissions(clock.GetUtcNow());
		var found = await repository.ListAsync(BeatmapQuery.Parse(query ?? string.Empty).LimitTo(effective), page!,
			context.RequestAborted);
		return Api.Paged(new Page<SetView>([.. found.Items.Select(SetView.Of)], found.Total), page!);
	}

	private static async Task<IResult> GetSetAsync(HttpContext context, int id, IBeatmapsetRepository sets,
		IBeatmapRepository beatmaps)
	{
		var set = await sets.GetAsync(id, context.RequestAborted);
		if (set is null || !CanView(context, set)) return Api.NotFound("No such beatmapset.");

		var list = await beatmaps.ListAsync(set, context.RequestAborted);
		return Api.Ok(new SetDetailView(SetView.Of(set), [.. list.Where(b => CanView(context, b)).OrderBy(b => b.Id).Select(b => BeatmapView.Of(b, false))]));
	}

	private static async Task<IResult> GetBeatmapAsync(HttpContext context, int id, IBeatmapRepository beatmaps)
	{
		var map = await beatmaps.GetAsync(id, context.RequestAborted);
		return map is null || !CanView(context, map)
			? Api.NotFound("No such beatmap.")
			: Api.Ok(BeatmapView.Of(map, true));
	}

	private static async Task<IResult> UploadAsync(HttpContext context, Server server)
	{
		if (!context.Request.HasFormContentType) return Api.Invalid("A multipart form with a file field is required.");

		var form = await context.Request.ReadFormAsync(context.RequestAborted);
		if (form.Files.GetFile("file") is not { } file) return Api.Invalid("The file field is missing.");

		await using var stream = file.OpenReadStream();
		var (result, import) = await server.ScopeTo(context.Connection()).Beatmapsets.UploadAsync(stream,
			context.RequestAborted);
		if (!result.IsOk) return result.Refusal();

		return import!.Failure switch
		{
			null => Api.Created(new SetDetailView(SetView.Of(import.Set!), [.. import.Beatmaps.Select(b => BeatmapView.Of(b, false))])),
			BeatmapsetImportFailure.AlreadyExists => Api.Error(StatusCodes.Status409Conflict, "alreadyExists",
				"The beatmapset already exists."),
			BeatmapsetImportFailure.Locked => Api.Error(StatusCodes.Status409Conflict, "locked",
				"The beatmapset is locked."),
			_ => Api.Invalid("The archive is not a readable beatmapset.", "unreadable")
		};
	}

	private static async Task<IResult> DeleteAsync(HttpContext context, int id, IBeatmapsetRepository sets,
		Beatmapsets beatmapsets)
	{
		return await sets.GetAsync(id, context.RequestAborted) is not { } set
			? Api.NotFound("No such beatmapset.")
			: (await beatmapsets.Of(set).ScopeTo(context.Connection()).DeleteAsync(context.RequestAborted)).Answer();
	}

	private static async Task<IResult> PatchAsync(HttpContext context, int id, PatchSetRequest request,
		IBeatmapsetRepository sets, Beatmapsets beatmapsets)
	{
		if (await sets.GetAsync(id, context.RequestAborted) is not { } set) return Api.NotFound("No such beatmapset.");
		if (request.Status is { } status && !Enum.IsDefined(status)) return Api.Invalid("status is not valid.");

		var scoped = beatmapsets.Of(set).ScopeTo(context.Connection());
		if (await ApplyPatchAsync(scoped, request, context.RequestAborted) is { } refusal) return refusal;

		return Api.Ok(SetView.Of(set), "Updated successfully");
	}

	private static async Task<IResult?> ApplyPatchAsync(ScopedBeatmapset scoped, PatchSetRequest request, CancellationToken ct)
	{
		if (request.Status is { } s && await scoped.SetStatusAsync(s, ct) is { IsOk: false } a) return a.Refusal();
		if (request.Visible is { } v && await scoped.SetVisibleAsync(v, ct) is { IsOk: false } b) return b.Refusal();
		if (request.Locked is { } l && await scoped.SetLockedAsync(l, ct) is { IsOk: false } c) return c.Refusal();
		return null;
	}

	private static async Task<IResult> SetFileAsync(HttpContext context, int id, IBeatmapsetRepository sets,
		IBeatmapRepository beatmaps, IBeatmapsetStorage storage, BeatmapsetFileType type,
		Func<BeatmapRecord, string?> file)
	{
		var set = await sets.GetAsync(id, context.RequestAborted);
		if (set is null || !CanView(context, set)) return Api.NotFound("No such beatmapset.");

		var map = (await beatmaps.ListAsync(set, context.RequestAborted)).OrderBy(b => b.Id)
			.FirstOrDefault(b => CanView(context, b) && file(b) is not null);
		return map is null ? Api.NotFound("No such file.") : await Serve(context, storage, set, type, file(map)!);
	}

	private static async Task<IResult> StoryboardAsync(HttpContext context, int id, IBeatmapsetRepository sets,
		IBeatmapsetStorage storage)
	{
		var set = await sets.GetAsync(id, context.RequestAborted);
		// shortcut: the stored name is the fixed storyboard name of the importer; read it from there if it ever varies.
		return set is null || !CanView(context, set) || !set.Assets.HasStoryboard
			? Api.NotFound("No such file.")
			: await Serve(context, storage, set, BeatmapsetFileType.Storyboard, "storyboard.osb");
	}

	private static async Task<IResult> DownloadAsync(HttpContext context, int id, IBeatmapsetRepository sets,
		IBeatmapsetStorage storage, bool? noVideo)
	{
		var set = await sets.GetAsync(id, context.RequestAborted);
		if (set is null || !CanView(context, set)) return Api.NotFound("No such beatmapset.");

		var archive = await storage.OpenArchiveAsync(set, noVideo != true, context.RequestAborted);
		return archive is null
			? Api.NotFound("No such file.")
			: NoSniff(context, Results.Stream(archive, "application/x-osu-beatmap-archive", $"{set.Id}.osz"));
	}

	private static async Task<IResult> RedirectToLowestAsync(HttpContext context, int id, string name,
		IBeatmapsetRepository sets, IBeatmapRepository beatmaps)
	{
		var set = await sets.GetAsync(id, context.RequestAborted);
		if (set is null || !CanView(context, set)) return Api.NotFound("No such beatmapset.");

		var lowest = (await beatmaps.ListAsync(set, context.RequestAborted)).OrderBy(b => b.Id)
			.FirstOrDefault(b => CanView(context, b));
		return lowest is null
			? Api.NotFound("The beatmapset has no beatmaps.")
			: Results.Redirect($"/v1/beatmaps/{lowest.Id}/{name}", false, true);
	}
}
