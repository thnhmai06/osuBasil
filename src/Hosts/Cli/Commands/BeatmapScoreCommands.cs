using Basil.Application.Beatmaps.Messages;
using Basil.Application.Beatmaps.Persistence;
using Basil.Application.Shared;
using Basil.Application.Scores.Messages;
using Basil.Application.Servers.Messages;
using Basil.Application.Shared.Paging;
using Basil.Application.Users.Persistence;
using Basil.Domain.Beatmaps;
using Basil.Domain.Shared;
using Basil.Domain.Cryptography;
using Basil.Domain.Users;
using Basil.Host.Shared.Views;
using Mediator;
using Basil.Application.Beatmaps.Objects;
using Basil.Application.Servers.Objects;

namespace Basil.Host.Cli.Commands;

/// <summary>The <c>beatmapsets</c>, <c>beatmaps</c> and <c>scores</c> commands.</summary>
internal static class BeatmapScoreCommands
{
	/// <summary>Creates the commands of this group.</summary>
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
			Pattern = "beatmapsets reload", Route = "POST /v1/beatmapsets/reload",
			Summary = "Recomputes the metadata of every beatmap, star rating included, in the background.",
			Run = ReloadAsync
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
		var found = await call.Get<ISender>().Send(new SearchBeatmapsets(await call.ByAsync(),
			BeatmapQuery.Parse(call.Text("query") ?? string.Empty), page), call.CancellationToken);
		call.PrintPage(new Page<SetView>([.. found.Items.Select(SetView.Of)], found.HasNext), page);
		return ExitCodes.Success;
	}

	private static async Task<int> ShowSetAsync(CommandCall call)
	{
		if (await call.Get<ISender>().Send(new GetBeatmapset(await call.ByAsync(), call.Id("id")),
			    call.CancellationToken) is not { } detail)
			return call.NotFound("beatmapset");

		call.Formatter.Print(call.Output,
			new SetDetailView(SetView.Of(detail.Set), [.. detail.Beatmaps.Select(b => BeatmapView.Of(b, false))]));
		return ExitCodes.Success;
	}

	private static async Task<int> UploadAsync(CommandCall call)
	{
		await using var stream = File.OpenRead(call.Required("file"));
		var (result, import) = await call.Get<ISender>().Send(
			new UploadBeatmapset(await call.ByAsync(), stream), call.CancellationToken);
		if (result != ServerOutcome.Ok) return call.Refuse(result);

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
		var status = call.Text("status") is { } text ? ParseStatus(text) : (BeatmapsetStatus?)null;

		var result = await call.Get<ISender>().Send(
			new PatchBeatmapset(await call.ByAsync(), call.Id("id"), status, call.Bool("visible"), call.Bool("locked")),
			call.CancellationToken);
		if (result != BeatmapsetOutcome.Ok) return call.Refuse(result);

		call.Output.Ok("Updated.");
		return ExitCodes.Success;
	}

	private static BeatmapsetStatus ParseStatus(string text)
	{
		return Enum.TryParse<BeatmapsetStatus>(text, true, out var parsed) && Enum.IsDefined(parsed)
			? parsed
			: throw CommandException.Invalid("--status is not a beatmapset status.");
	}

	private static async Task<int> ReloadAsync(CommandCall call)
	{
		return call.Report(
			await call.Get<ISender>().Send(new ReloadBeatmaps(await call.ByAsync()), call.CancellationToken),
			RulesetReplies.ReloadStarted);
	}

	private static async Task<int> DeleteSetAsync(CommandCall call)
	{
		return call.Report(
			await call.Get<ISender>().Send(new DeleteBeatmapset(await call.ByAsync(), call.Id("id")),
				call.CancellationToken), "Beatmapset deleted.");
	}

	private static async Task<int> ShowBeatmapAsync(CommandCall call)
	{
		if (await call.Get<ISender>().Send(new GetBeatmap(await call.ByAsync(), call.Id("id")),
			    call.CancellationToken) is not { } map)
			return call.NotFound("beatmap");

		call.Formatter.Print(call.Output, BeatmapView.Of(map, true));
		return ExitCodes.Success;
	}

	private static async Task<int> ListScoresAsync(CommandCall call)
	{
		var page = call.Page();
		var listing = await call.Get<ISender>().Send(new ListScores(call.Int("beatmap"), call.Int("user"), page),
			call.CancellationToken);
		switch (listing.Outcome)
		{
			case ScoreListOutcome.NoBeatmap:
				return call.NotFound("beatmap");
			case ScoreListOutcome.NoUser:
				return call.NotFound("user");
		}

		call.PrintPage(new Page<ScoreView>([.. listing.Scores!.Items.Select(ScoreView.Of)], listing.Scores.HasNext),
			page);
		return ExitCodes.Success;
	}

	private static async Task<int> ShowScoreAsync(CommandCall call)
	{
		if (await call.Get<ISender>().Send(new GetScore(call.Id("id")), call.CancellationToken) is not { } score)
			return call.NotFound("score");

		call.Formatter.Print(call.Output, ScoreView.Of(score));
		return ExitCodes.Success;
	}
}