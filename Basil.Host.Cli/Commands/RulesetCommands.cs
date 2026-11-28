using Basil.Application.Beatmaps.Messages;
using Basil.Application.Rulesets.Messages;
using Basil.Application.Rulesets.Objects;
using Basil.Application.Rulesets.Services;
using Basil.Host.Shared.Info;
using Mediator;

namespace Basil.Host.Cli.Commands;

public static class RulesetReplies
{
	public const string Installed =
		"Installed the osu! calculation stack {0}. Waiting beatmapsets are being imported and missing star ratings calculated. Run `scores recalculate` for the pp of stored scores.";

	public const string InstalledRestart =
		"Repaired the osu! calculation stack {0}. Restart the server to use the repaired files.";

	public const string UpToDate = "The osu! calculation stack is already installed ({0}).";

	public const string UpdateFailedCode = "rulesetUpdateFailed";

	public const string ReloadStarted = "Reloading beatmaps in the background.";
}

internal static class RulesetCommands
{
	public static IEnumerable<CommandDef> Create()
	{
		yield return new CommandDef
		{
			Pattern = "version", Route = "GET /v1/version", Offline = true, NeedsStorage = false,
			Summary = "Shows the application version and the state of the osu! calculation stack.", Run = VersionAsync
		};
		yield return new CommandDef
		{
			Pattern = "ruleset version", Route = "GET /v1/settings/ruleset/version", Offline = true,
			NeedsStorage = false,
			Summary = "Shows the version of the osu! calculation stack and whether it is installed.",
			Run = RulesetVersionAsync
		};
		yield return new CommandDef
		{
			Pattern = "ruleset update", Route = "POST /v1/settings/ruleset/update", Offline = true,
			NeedsStorage = false,
			Summary = "Downloads and installs the osu! calculation stack when it is missing or damaged.",
			Run = UpdateAsync
		};
	}

	private static Task<int> VersionAsync(CommandCall call)
	{
		call.Output.Fields([
			("version", AppVersion.Current),
			("ruleset", RulesetOf(call.Get<IRulesetUpdater>().Version()))
		]);
		return Task.FromResult(ExitCodes.Success);
	}

	private static string RulesetOf(RulesetVersions versions)
	{
		return $"{versions.Version} ({versions.State})";
	}

	private static async Task<int> RulesetVersionAsync(CommandCall call)
	{
		var (result, versions) = await call.Get<ISender>().Send(new GetRulesetVersion(await call.ByAsync()),
			call.CancellationToken);
		if (result != RulesetUpdateResult.Ok) return call.Refuse(result);

		call.Output.Fields([("version", versions!.Version), ("state", versions.State.ToString())]);
		return ExitCodes.Success;
	}

	private static async Task<int> UpdateAsync(CommandCall call)
	{
		try
		{
			var (result, install) = await call.Get<ISender>().Send(new UpdateRulesets(await call.ByAsync()),
				call.CancellationToken);
			if (result != RulesetUpdateResult.Ok) return call.Refuse(result);

			call.Output.Ok(string.Format(
				!install!.Downloaded ? RulesetReplies.UpToDate :
				install.RestartRequired ? RulesetReplies.InstalledRestart : RulesetReplies.Installed,
				install.Version));
			return ExitCodes.Success;
		}
		catch (RulesetUpdateException e)
		{
			call.Output.Error(RulesetReplies.UpdateFailedCode, e.Message);
			return ExitCodes.Refused;
		}
	}
}