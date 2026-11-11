using Basil.Application.Beatmaps.Messages;
using Basil.Application.Beatmaps.Services;
using Basil.Host.Shared;
using Mediator;
using Basil.Application.Beatmaps.Objects;

namespace Basil.Host.Cli.Commands;

/// <summary>What the <c>version</c> and <c>ruleset</c> commands say.</summary>
public static class RulesetReplies
{
	/// <summary>Printed after a new ruleset version was staged.</summary>
	public const string Staged =
		"Staged the osu! rulesets {0}. Restart the server for them to take effect, then run `beatmapsets reload` to recompute the stored star ratings.";

	/// <summary>Printed when no newer ruleset version exists.</summary>
	public const string UpToDate = "The osu! rulesets are up to date ({0}).";

	/// <summary>The code of the error printed when the ruleset feed fails.</summary>
	public const string UpdateFailedCode = "rulesetUpdateFailed";

	/// <summary>Printed when a beatmap reload was started.</summary>
	public const string ReloadStarted = "Reloading beatmaps in the background.";
}

/// <summary>The <c>version</c> and <c>ruleset</c> commands.</summary>
internal static class RulesetCommands
{
	/// <summary>Creates the commands of this group.</summary>
	public static IEnumerable<CommandDef> Create()
	{
		yield return new CommandDef
		{
			Pattern = "version", Route = "GET /v1/version", Offline = true, NeedsStorage = false,
			Summary = "Shows the application version and the active ruleset version.", Run = VersionAsync
		};
		yield return new CommandDef
		{
			Pattern = "ruleset version", Route = "GET /v1/settings/ruleset/version", Offline = true,
			NeedsStorage = false,
			Summary = "Shows the active ruleset version and the version waiting for a restart.",
			Run = RulesetVersionAsync
		};
		yield return new CommandDef
		{
			Pattern = "ruleset update", Route = "POST /v1/settings/ruleset/update", Offline = true,
			NeedsStorage = false,
			Summary = "Downloads the newest osu! rulesets and stages them for the next restart.",
			Run = UpdateAsync
		};
	}

	private static Task<int> VersionAsync(CommandCall call)
	{
		call.Output.Fields([
			("version", AppVersion.Current),
			("ruleset", call.Get<IRulesetUpdater>().Version().Active)
		]);
		return Task.FromResult(ExitCodes.Success);
	}

	private static async Task<int> RulesetVersionAsync(CommandCall call)
	{
		var (result, versions) = await call.Get<ISender>().Send(new GetRulesetVersion(await call.ByAsync()),
			call.CancellationToken);
		if (result != RulesetOutcome.Ok) return call.Refuse(result);

		call.Output.Fields([("active", versions!.Active), ("staged", versions.Staged ?? "none")]);
		return ExitCodes.Success;
	}

	private static async Task<int> UpdateAsync(CommandCall call)
	{
		try
		{
			var (result, stage) = await call.Get<ISender>().Send(new UpdateRulesets(await call.ByAsync()),
				call.CancellationToken);
			if (result != RulesetOutcome.Ok) return call.Refuse(result);

			call.Output.Ok(string.Format(stage!.Staged ? RulesetReplies.Staged : RulesetReplies.UpToDate,
				stage.Version));
			return ExitCodes.Success;
		}
		catch (RulesetUpdateException e)
		{
			call.Output.Error(RulesetReplies.UpdateFailedCode, e.Message);
			return ExitCodes.Refused;
		}
	}
}