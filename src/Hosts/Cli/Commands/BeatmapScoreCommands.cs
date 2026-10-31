using Basil.Application.Beatmaps.Capabilities;
using Basil.Application.Beatmaps.Objects;
using Basil.Application.Beatmaps.Persistence;
using Basil.Application.Common.Common;
using Basil.Application.Scores.Persistence;
using Basil.Application.Servers.Objects;
using Basil.Application.Users.Persistence;
using Basil.Domain.Beatmaps;
using Basil.Domain.Common;
using Basil.Domain.Users;
using Basil.Host.Shared.Views;

namespace Basil.Host.Cli.Commands;

/// <summary>The <c>beatmapsets</c>, <c>beatmaps</c> and <c>scores</c> commands.</summary>
internal static class BeatmapScoreCommands
{
	public static IEnumerable<CommandDef> Create()
	{
		yield return new CommandDef
		{
			Pattern = "beatmapsets list", Route = "GET /v1/beatmapsets", Offline = true,
			Summary = "Searches beatmapsets.",
			Options = [new OptionDef("query", "The beatmap search syntax."), .. CommandHelpers.Paging],
			Run = ListSetsAsync
		};
		yield return new CommandDef
		{
			Pattern = "beatmapsets <id:int>", Route = "GET /v1/beatmapsets/{id}", Offline = true,
			Summary = "Shows a beatmapset with its beatmaps.", Run = ShowSetAsync
		};
		yield return new CommandDef
		{
			Pattern = "beatmapsets create", Route = "POST /v1/beatmapsets", Offline = true,
			Summary = "Imports a beatmapset from an .osz archive.",
			Options = [new OptionDef("file", "The path of the .osz archive.", OptionKind.Path, true)], Run = UploadAsync
		};
		yield return new CommandDef
		{
			Pattern = "beatmapsets <id:int> set", Route = "PATCH /v1/beatmapsets/{id}", Offline = true,
			Summary = "Changes the status, visibility or lock of a beatmapset.",
			Options =
			[
				new OptionDef("status", "ranked, approved, qualified, loved, pending, wip or graveyard."),
				new OptionDef("visible", "Whether the set is shown.", OptionKind.Bool),
				new OptionDef("locked", "Whether the set is write-locked.", OptionKind.Bool)
			],
			Run = PatchSetAsync
		};
		yield return new CommandDef
		{
			Pattern = "beatmapsets refresh", Offline = true,
			Summary = "Scans Data/Beatmapsets as at startup: takes in sets added by hand and drops removed ones.",
			Run = RefreshAsync
		};
		yield return new CommandDef
		{
			Pattern = "beatmapsets <id:int> delete", Route = "DELETE /v1/beatmapsets/{id}", Offline = true,
			Summary = "Deletes a beatmapset.", Run = DeleteSetAsync
		};
		yield return new CommandDef
		{
			Pattern = "beatmaps <id:int>", Route = "GET /v1/beatmaps/{id}", Offline = true,
			Summary = "Shows a beatmap with its beatmapset.", Run = ShowBeatmapAsync
		};
		yield return new CommandDef
		{
			Pattern = "scores list", Route = "GET /v1/scores", Offline = true, Summary = "Lists scores, newest first.",
			Options =
			[
				new OptionDef("beatmap", "A beatmap id.", OptionKind.Int),
				new OptionDef("user", "A user id.", OptionKind.Int), .. CommandHelpers.Paging
			],
			Run = ListScoresAsync
		};
		yield return new CommandDef
		{
			Pattern = "scores <id:int>", Route = "GET /v1/scores/{id}", Offline = true, Summary = "Shows a score.",
			Run = ShowScoreAsync
		};
	}

	private static async Task<int> ListSetsAsync(CommandCall call)
	{
		var page = call.Page();
		var found = await call.Get<IBeatmapsetRepository>().ListAsync(
			BeatmapQuery.Parse(call.Text("query") ?? string.Empty).LimitTo(UserPermissionsGroups.All), page,
			call.CancellationToken);
		call.PrintPage(new Page<SetView>([.. found.Items.Select(SetView.Of)], found.Total), page);
		return ExitCodes.Success;
	}

	private static async Task<int> ShowSetAsync(CommandCall call)
	{
		if (await call.Get<IBeatmapsetRepository>().GetAsync(call.Id("id"), call.CancellationToken) is not { } set)
			return call.NotFound("beatmapset");

		var list = await call.Get<IBeatmapRepository>().ListAsync(set, call.CancellationToken);
		call.Formatter.Print(call.Output,
			new SetDetailView(SetView.Of(set), [.. list.OrderBy(b => b.Id).Select(b => BeatmapView.Of(b, false))]));
		return ExitCodes.Success;
	}

	private static async Task<int> UploadAsync(CommandCall call)
	{
		await using var stream = File.OpenRead(call.Required("file"));
		var (result, import) = await call.Get<Server>().ScopeTo(await call.ByAsync()).Beatmapsets
			.UploadAsync(stream, call.CancellationToken);
		if (!result.IsOk) return call.Refuse(result);

		switch (import!.Failure)
		{
			case null:
				call.Output.Ok($"Imported beatmapset {import.Set!.Id} with {import.Beatmaps.Count} beatmaps.");
				return ExitCodes.Success;
			case BeatmapsetImportFailure.AlreadyExists:
				call.Output.Error("alreadyExists", "The beatmapset already exists.");
				return ExitCodes.Refused;
			case BeatmapsetImportFailure.Locked:
				call.Output.Error("locked", "The beatmapset is locked.");
				return ExitCodes.Refused;
			default:
				call.Output.Error("unreadable", "The archive is not a readable beatmapset.");
				return ExitCodes.Invalid;
		}
	}

	private static async Task<int> PatchSetAsync(CommandCall call)
	{
		if (await call.Get<IBeatmapsetRepository>().GetAsync(call.Id("id"), call.CancellationToken) is not { } set)
			return call.NotFound("beatmapset");

		var status = call.Text("status") is { } text ? ParseStatus(text) : (BeatmapsetStatus?)null;

		var scoped = call.Get<Beatmapsets>().Of(set).ScopeTo(await call.ByAsync());
		if (await ApplyPatchAsync(call, scoped, status) is { } refused) return refused;

		call.Output.Ok("Updated.");
		return ExitCodes.Success;
	}

	private static BeatmapsetStatus ParseStatus(string text)
	{
		return Enum.TryParse<BeatmapsetStatus>(text, true, out var parsed) && Enum.IsDefined(parsed)
			? parsed
			: throw CommandException.Invalid("--status is not a beatmapset status.");
	}

	private static async Task<int?> ApplyPatchAsync(CommandCall call, ScopedBeatmapset scoped, BeatmapsetStatus? status)
	{
		var ct = call.CancellationToken;
		if (status is { } s && await scoped.SetStatusAsync(s, ct) is { IsOk: false } a) return call.Refuse(a);
		if (call.Bool("visible") is { } v && await scoped.SetVisibleAsync(v, ct) is { IsOk: false } b)
			return call.Refuse(b);
		if (call.Bool("locked") is { } l && await scoped.SetLockedAsync(l, ct) is { IsOk: false } c)
			return call.Refuse(c);
		return null;
	}

	private static async Task<int> RefreshAsync(CommandCall call)
	{
		var result = await call.Get<Beatmapsets>().ScanAsync(call.CancellationToken);
		call.Output.Ok($"Scanned: {result.Imported} taken in, {result.Updated} updated, {result.Forgotten} forgotten.");
		return ExitCodes.Success;
	}

	private static async Task<int> DeleteSetAsync(CommandCall call)
	{
		return await call.Get<IBeatmapsetRepository>().GetAsync(call.Id("id"), call.CancellationToken) is not { } set
			? call.NotFound("beatmapset")
			: call.Report(
				await call.Get<Beatmapsets>().Of(set).ScopeTo(await call.ByAsync()).DeleteAsync(call.CancellationToken),
				"Beatmapset deleted.");
	}

	private static async Task<int> ShowBeatmapAsync(CommandCall call)
	{
		if (await call.Get<IBeatmapRepository>().GetAsync(call.Id("id"), call.CancellationToken) is not { } map)
			return call.NotFound("beatmap");

		call.Formatter.Print(call.Output, BeatmapView.Of(map, true));
		return ExitCodes.Success;
	}

	private static async Task<int> ListScoresAsync(CommandCall call)
	{
		var page = call.Page();
		var ct = call.CancellationToken;
		Md5? hash = null;
		if (call.Int("beatmap") is { } beatmapId)
		{
			if (await call.Get<IBeatmapRepository>().GetAsync(beatmapId, ct) is not { } map)
				return call.NotFound("beatmap");
			hash = map.Hash;
		}

		UserRecord? player = null;
		if (call.Int("user") is { } userId && (player = await call.Get<IUserRepository>().GetAsync(userId, ct)) is null)
			return call.NotFound("user");

		var found = await call.Get<IScoreRepository>().ListAsync(new ScoreQuery(player, hash), page, ct);
		call.PrintPage(new Page<ScoreView>([.. found.Items.Select(ScoreView.Of)], found.Total), page);
		return ExitCodes.Success;
	}

	private static async Task<int> ShowScoreAsync(CommandCall call)
	{
		if (await call.Get<IScoreRepository>().GetAsync(call.Id("id"), call.CancellationToken) is not { } score)
			return call.NotFound("score");

		call.Formatter.Print(call.Output, ScoreView.Of(score));
		return ExitCodes.Success;
	}
}